using JadeClient.Exceptions;
using JadeClient.Models;
using JadeClient.PinServer;
using JadeClient.Transport;

namespace JadeClient.Protocol;

/// <summary>
/// Low-level RPC communication layer for Jade device.
/// Handles request/response serialization and transport.
/// </summary>
public class JadeRpc : IDisposable
{
    private readonly IJadeTransport _transport;
    private readonly TimeSpan _defaultTimeout;
    private readonly bool _ownsTransport;
    private int _requestId;
    private bool _disposed;

    /// <summary>
    /// Creates a new JadeRpc instance.
    /// </summary>
    /// <param name="transport">Transport layer for device communication.</param>
    /// <param name="defaultTimeout">Default timeout for RPC operations.</param>
    /// <param name="ownsTransport">If true, the transport will be disposed when this instance is disposed.</param>
    public JadeRpc(IJadeTransport transport, TimeSpan? defaultTimeout = null, bool ownsTransport = false)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _defaultTimeout = defaultTimeout ?? TimeSpan.FromSeconds(30);
        _ownsTransport = ownsTransport;
        _requestId = 0;
    }

    /// <summary>
    /// Gets whether the transport is connected.
    /// </summary>
    public bool IsConnected => _transport.IsConnected;

    /// <summary>
    /// Connect to the Jade device.
    /// </summary>
    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _transport.ConnectAsync(cancellationToken);
    }

    /// <summary>
    /// Disconnect from the Jade device.
    /// </summary>
    public Task DisconnectAsync()
    {
        return _transport.DisconnectAsync();
    }

    /// <summary>
    /// Send an RPC request and wait for the response.
    /// </summary>
    /// <param name="method">RPC method name.</param>
    /// <param name="parameters">Optional method parameters.</param>
    /// <param name="timeout">Optional timeout override.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The RPC response.</returns>
    public async Task<RpcResponse> CallAsync(
        string method,
        Dictionary<string, object>? parameters = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!_transport.IsConnected)
            throw new JadeConnectionException("Not connected to device");

        var request = new RpcRequest
        {
            Id = GenerateRequestId(),
            Method = method,
            Params = parameters
        };

        var requestBytes = CborSerializer.SerializeRequest(request);

        var effectiveTimeout = timeout ?? _defaultTimeout;
        using var timeoutCts = new CancellationTokenSource(effectiveTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            // Send request
            await _transport.WriteAsync(requestBytes, linkedCts.Token);

            // Wait for response
            var responseBytes = await _transport.ReadAsync(linkedCts.Token);
            var response = CborSerializer.DeserializeResponse(responseBytes);

            // Verify response ID matches request
            if (response.Id != request.Id)
            {
                throw new JadeException($"Response ID mismatch: expected '{request.Id}', got '{response.Id}'");
            }

            return response;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"RPC call '{method}' timed out after {effectiveTimeout.TotalSeconds} seconds");
        }
    }

    /// <summary>
    /// Send an RPC request and extract the result, throwing on error.
    /// </summary>
    /// <typeparam name="T">Expected result type.</typeparam>
    /// <param name="method">RPC method name.</param>
    /// <param name="parameters">Optional method parameters.</param>
    /// <param name="timeout">Optional timeout override.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result value.</returns>
    public async Task<T> CallAsync<T>(
        string method,
        Dictionary<string, object>? parameters = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var response = await CallAsync(method, parameters, timeout, cancellationToken);

        if (!response.IsSuccess)
        {
            throw new JadeRpcException(
                response.Error!.Code,
                response.Error.Message,
                response.Error.Data);
        }

        return ConvertResult<T>(response.Result);
    }

    /// <summary>
    /// Get device version information.
    /// </summary>
    public async Task<VersionInfo> GetVersionInfoAsync(CancellationToken cancellationToken = default)
    {
        var result = await CallAsync<Dictionary<string, object?>>("get_version_info", cancellationToken: cancellationToken);
        return ParseVersionInfo(result);
    }

    /// <summary>
    /// Add entropy to the device RNG.
    /// </summary>
    /// <param name="entropy">Random bytes to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<bool> AddEntropyAsync(byte[] entropy, CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, object>
        {
            ["entropy"] = entropy
        };

        return await CallAsync<bool>("add_entropy", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Authenticate user with PIN via the PIN server.
    /// This initiates the Blind Oracle protocol to unlock the device.
    /// </summary>
    /// <param name="pinServerHandler">PIN server handler (remote or local).</param>
    /// <param name="network">Network to authenticate for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if authentication succeeded, false otherwise.</returns>
    public async Task<bool> AuthUserAsync(
        IPinServerHandler pinServerHandler,
        string network = "mainnet",
        CancellationToken cancellationToken = default)
    {
        if (pinServerHandler == null)
            throw new ArgumentNullException(nameof(pinServerHandler));

        // Extended timeout for user interaction (PIN entry on device)
        var interactiveTimeout = TimeSpan.FromMinutes(5);

        // Send initial auth_user request
        var parameters = new Dictionary<string, object>
        {
            ["network"] = network,
            ["epoch"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        var response = await CallAsync("auth_user", parameters, interactiveTimeout, cancellationToken);

        // Handle http_request loop (may require multiple round-trips with PIN server)
        while (response.HasHttpRequest)
        {
            var httpRequest = CborSerializer.ExtractHttpRequest(response.Result);
            if (httpRequest == null)
            {
                throw new JadeException("Failed to extract http_request from response");
            }

            // Extract the endpoint from the URL
            var endpoint = ExtractEndpoint(httpRequest.Urls.FirstOrDefault() ?? "");

            // Process via PIN server handler (remote or local)
            string pinServerResponse;
            try
            {
                pinServerResponse = await pinServerHandler.ProcessRequestAsync(
                    endpoint,
                    httpRequest.Data,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                throw new JadeException($"PIN server request failed: {ex.Message}", ex);
            }

            // Parse the PIN server JSON response and send it back to Jade
            // The response body becomes the params for the on-reply RPC call
            Dictionary<string, object>? replyParams = null;
            try
            {
                replyParams = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(pinServerResponse);
            }
            catch
            {
                // If not valid JSON, wrap it
                replyParams = new Dictionary<string, object> { ["body"] = pinServerResponse };
            }

            response = await CallAsync(httpRequest.OnReply, replyParams, interactiveTimeout, cancellationToken);
        }

        // Check final result
        if (!response.IsSuccess)
        {
            if (response.Error != null)
            {
                throw new JadeRpcException(
                    response.Error.Code,
                    response.Error.Message,
                    response.Error.Data);
            }
            return false;
        }

        // Result should be true for successful authentication
        return response.Result is bool success && success;
    }

    /// <summary>
    /// Logout from the device (lock the wallet).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<bool> LogoutAsync(CancellationToken cancellationToken = default)
    {
        var response = await CallAsync("logout", cancellationToken: cancellationToken);
        return response.IsSuccess;
    }

    /// <summary>
    /// Set the wallet mnemonic (DEBUG builds only, e.g., QEMU emulator).
    /// </summary>
    /// <param name="mnemonic">BIP39 mnemonic phrase (12 or 24 words).</param>
    /// <param name="passphrase">Optional BIP39 passphrase.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the mnemonic was set successfully.</returns>
    public async Task<bool> SetMnemonicAsync(
        string mnemonic,
        string? passphrase = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, object>
        {
            ["mnemonic"] = mnemonic
        };

        if (!string.IsNullOrEmpty(passphrase))
        {
            parameters["passphrase"] = passphrase;
        }

        return await CallAsync<bool>("debug_set_mnemonic", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get an extended public key (xpub) for a derivation path.
    /// </summary>
    /// <param name="network">Network (e.g., "mainnet", "testnet").</param>
    /// <param name="path">BIP32 derivation path as an array of integers.
    /// Use values with 0x80000000 added for hardened derivation (e.g., 84' = 2147483732).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The extended public key as a base58-encoded string.</returns>
    public async Task<string> GetXpubAsync(
        string network,
        uint[] path,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, object>
        {
            ["network"] = network,
            ["path"] = path.Select(p => (object)p).ToArray()
        };

        return await CallAsync<string>("get_xpub", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get a receive address for a given derivation path and address type.
    /// The address will be displayed on the Jade screen for user verification.
    /// </summary>
    /// <param name="network">Network (e.g., "mainnet", "testnet").</param>
    /// <param name="path">Full BIP32 derivation path including account and address index
    /// (e.g., m/84'/0'/0'/0/0 for the first receiving address).</param>
    /// <param name="variant">Address type variant:
    /// - "pkh(k)" for Legacy P2PKH (BIP44)
    /// - "sh(wpkh(k))" for Nested SegWit P2SH-P2WPKH (BIP49)
    /// - "wpkh(k)" for Native SegWit P2WPKH (BIP84)
    /// - "tr(k)" for Taproot P2TR (BIP86)</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The generated address string.</returns>
    public async Task<string> GetReceiveAddressAsync(
        string network,
        uint[] path,
        string variant,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, object>
        {
            ["network"] = network,
            ["path"] = path.Select(p => (object)p).ToArray(),
            ["variant"] = variant
        };

        return await CallAsync<string>("get_receive_address", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Update the PIN server configuration on the device.
    /// This is required when switching to a custom or local PIN server.
    /// </summary>
    /// <param name="urlA">Primary PIN server URL.</param>
    /// <param name="urlB">Secondary PIN server URL (e.g., Tor onion address). Can be null.</param>
    /// <param name="pubkey">Server's public key (33 bytes, compressed secp256k1).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<bool> UpdatePinServerAsync(
        string urlA,
        string? urlB,
        byte[] pubkey,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, object>
        {
            ["urlA"] = urlA,
            ["pubkey"] = pubkey
        };

        if (!string.IsNullOrEmpty(urlB))
        {
            parameters["urlB"] = urlB;
        }

        return await CallAsync<bool>("update_pinserver", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Reset the PIN server configuration to Blockstream defaults.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<bool> ResetPinServerAsync(CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, object>
        {
            ["reset_details"] = true,
            ["reset_certificate"] = true
        };

        return await CallAsync<bool>("update_pinserver", parameters, cancellationToken: cancellationToken);
    }

    #region HSM Mode Methods

    /// <summary>
    /// Get HSM mode status and information.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>HSM info including active status, networks, paths, and operations count.</returns>
    public async Task<HsmInfo> HsmGetInfoAsync(CancellationToken cancellationToken = default)
    {
        var result = await CallAsync<Dictionary<string, object?>>("hsm_get_info", cancellationToken: cancellationToken);
        return ParseHsmInfo(result);
    }

    /// <summary>
    /// Get a public key from the HSM at a specific index.
    /// </summary>
    /// <param name="network">Network ("mainnet" or "testnet").</param>
    /// <param name="index">Key index (non-hardened, 0 to 2^31-1).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The public key (33 bytes, compressed) and derivation path.</returns>
    public async Task<HsmPubkeyResult> HsmGetPubkeyAsync(
        string network,
        uint index,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, object>
        {
            ["network"] = network,
            ["index"] = index
        };

        var result = await CallAsync<Dictionary<string, object?>>("hsm_get_pubkey", parameters, cancellationToken: cancellationToken);
        return new HsmPubkeyResult
        {
            Pubkey = result.TryGetValue("pubkey", out var pk) && pk is byte[] pubkeyBytes ? pubkeyBytes : Array.Empty<byte>(),
            Path = result.TryGetValue("path", out var path) ? path?.ToString() ?? "" : ""
        };
    }

    /// <summary>
    /// Get an extended public key (xpub) for the HSM root key.
    /// </summary>
    /// <param name="network">Network ("mainnet" or "testnet").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The xpub string and derivation path.</returns>
    public async Task<HsmXpubResult> HsmGetXpubAsync(
        string network,
        CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, object>
        {
            ["network"] = network
        };

        var result = await CallAsync<Dictionary<string, object?>>("hsm_get_xpub", parameters, cancellationToken: cancellationToken);
        return new HsmXpubResult
        {
            Xpub = result.TryGetValue("xpub", out var xpub) ? xpub?.ToString() ?? "" : "",
            Path = result.TryGetValue("path", out var path) ? path?.ToString() ?? "" : ""
        };
    }

    /// <summary>
    /// Sign a 32-byte hash using an HSM key.
    /// </summary>
    /// <param name="network">Network ("mainnet" or "testnet").</param>
    /// <param name="index">Key index (non-hardened).</param>
    /// <param name="hash">32-byte hash to sign.</param>
    /// <param name="algorithm">Signature algorithm ("schnorr" or "ecdsa"). Defaults to "schnorr".</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The signature, public key, and algorithm used.</returns>
    public async Task<HsmSignResult> HsmSignAsync(
        string network,
        uint index,
        byte[] hash,
        string algorithm = "schnorr",
        CancellationToken cancellationToken = default)
    {
        if (hash.Length != 32)
            throw new ArgumentException("Hash must be 32 bytes", nameof(hash));

        var parameters = new Dictionary<string, object>
        {
            ["network"] = network,
            ["index"] = index,
            ["hash"] = hash,
            ["algo"] = algorithm
        };

        var result = await CallAsync<Dictionary<string, object?>>("hsm_sign", parameters, cancellationToken: cancellationToken);
        return new HsmSignResult
        {
            Signature = result.TryGetValue("signature", out var sig) && sig is byte[] sigBytes ? sigBytes : Array.Empty<byte>(),
            Pubkey = result.TryGetValue("pubkey", out var pk) && pk is byte[] pubkeyBytes ? pubkeyBytes : Array.Empty<byte>(),
            Algorithm = result.TryGetValue("algo", out var algo) ? algo?.ToString() ?? "schnorr" : "schnorr"
        };
    }

    /// <summary>
    /// Compute an ECDH shared secret using an HSM key.
    /// </summary>
    /// <param name="network">Network ("mainnet" or "testnet").</param>
    /// <param name="index">Key index (non-hardened).</param>
    /// <param name="theirPubkey">The other party's public key (33 or 65 bytes).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The 32-byte shared secret.</returns>
    public async Task<byte[]> HsmEcdhAsync(
        string network,
        uint index,
        byte[] theirPubkey,
        CancellationToken cancellationToken = default)
    {
        if (theirPubkey.Length != 33 && theirPubkey.Length != 65)
            throw new ArgumentException("Public key must be 33 (compressed) or 65 (uncompressed) bytes", nameof(theirPubkey));

        var parameters = new Dictionary<string, object>
        {
            ["network"] = network,
            ["index"] = index,
            ["their_pubkey"] = theirPubkey
        };

        var result = await CallAsync<Dictionary<string, object?>>("hsm_ecdh", parameters, cancellationToken: cancellationToken);
        return result.TryGetValue("shared_secret", out var ss) && ss is byte[] secretBytes ? secretBytes : Array.Empty<byte>();
    }

    /// <summary>
    /// Encrypt data using ECIES with an HSM key.
    /// </summary>
    /// <param name="network">Network ("mainnet" or "testnet").</param>
    /// <param name="index">Key index (non-hardened).</param>
    /// <param name="plaintext">Data to encrypt (max 1024 bytes).</param>
    /// <param name="theirPubkey">Optional recipient public key. If null, encrypts to self.</param>
    /// <param name="aad">Optional additional authenticated data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Encrypted data components (ciphertext, nonce, tag, ephemeral_pubkey).</returns>
    public async Task<HsmEncryptResult> HsmEncryptAsync(
        string network,
        uint index,
        byte[] plaintext,
        byte[]? theirPubkey = null,
        byte[]? aad = null,
        CancellationToken cancellationToken = default)
    {
        if (plaintext.Length > 1024)
            throw new ArgumentException("Plaintext must not exceed 1024 bytes", nameof(plaintext));

        var parameters = new Dictionary<string, object>
        {
            ["network"] = network,
            ["index"] = index,
            ["plaintext"] = plaintext
        };

        if (theirPubkey != null)
            parameters["their_pubkey"] = theirPubkey;
        if (aad != null)
            parameters["aad"] = aad;

        var result = await CallAsync<Dictionary<string, object?>>("hsm_encrypt", parameters, cancellationToken: cancellationToken);
        return new HsmEncryptResult
        {
            Ciphertext = result.TryGetValue("ciphertext", out var ct) && ct is byte[] ctBytes ? ctBytes : Array.Empty<byte>(),
            Nonce = result.TryGetValue("nonce", out var n) && n is byte[] nonceBytes ? nonceBytes : Array.Empty<byte>(),
            Tag = result.TryGetValue("tag", out var t) && t is byte[] tagBytes ? tagBytes : Array.Empty<byte>(),
            EphemeralPubkey = result.TryGetValue("ephemeral_pubkey", out var ep) && ep is byte[] epBytes ? epBytes : Array.Empty<byte>()
        };
    }

    /// <summary>
    /// Decrypt data using ECIES with an HSM key.
    /// </summary>
    /// <param name="network">Network ("mainnet" or "testnet").</param>
    /// <param name="index">Key index (non-hardened).</param>
    /// <param name="ciphertext">Encrypted data.</param>
    /// <param name="nonce">12-byte nonce.</param>
    /// <param name="tag">16-byte authentication tag.</param>
    /// <param name="ephemeralPubkey">33-byte ephemeral public key.</param>
    /// <param name="aad">Optional additional authenticated data (must match encryption).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The decrypted plaintext.</returns>
    public async Task<byte[]> HsmDecryptAsync(
        string network,
        uint index,
        byte[] ciphertext,
        byte[] nonce,
        byte[] tag,
        byte[] ephemeralPubkey,
        byte[]? aad = null,
        CancellationToken cancellationToken = default)
    {
        if (nonce.Length != 12)
            throw new ArgumentException("Nonce must be 12 bytes", nameof(nonce));
        if (tag.Length != 16)
            throw new ArgumentException("Tag must be 16 bytes", nameof(tag));
        if (ephemeralPubkey.Length != 33)
            throw new ArgumentException("Ephemeral public key must be 33 bytes", nameof(ephemeralPubkey));

        var parameters = new Dictionary<string, object>
        {
            ["network"] = network,
            ["index"] = index,
            ["ciphertext"] = ciphertext,
            ["nonce"] = nonce,
            ["tag"] = tag,
            ["ephemeral_pubkey"] = ephemeralPubkey
        };

        if (aad != null)
            parameters["aad"] = aad;

        var result = await CallAsync<Dictionary<string, object?>>("hsm_decrypt", parameters, cancellationToken: cancellationToken);
        return result.TryGetValue("plaintext", out var pt) && pt is byte[] ptBytes ? ptBytes : Array.Empty<byte>();
    }

    /// <summary>
    /// Lock/deactivate HSM mode.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if successfully locked.</returns>
    public async Task<bool> HsmLockAsync(CancellationToken cancellationToken = default)
    {
        return await CallAsync<bool>("hsm_lock", cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Encrypt data using BIE1 ECIES (NBitcoin compatible).
    /// </summary>
    /// <param name="network">Network ("mainnet" or "testnet").</param>
    /// <param name="index">Key index (non-hardened).</param>
    /// <param name="plaintext">Data to encrypt (max 1024 bytes).</param>
    /// <param name="theirPubkey">Optional recipient public key. If null, encrypts to self.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>BIE1 encrypted blob: "BIE1" + ephemeral_pubkey(33) + ciphertext + hmac(32).</returns>
    public async Task<byte[]> HsmEncryptBie1Async(
        string network,
        uint index,
        byte[] plaintext,
        byte[]? theirPubkey = null,
        CancellationToken cancellationToken = default)
    {
        if (plaintext.Length > 1024)
            throw new ArgumentException("Plaintext must not exceed 1024 bytes", nameof(plaintext));

        var parameters = new Dictionary<string, object>
        {
            ["network"] = network,
            ["index"] = index,
            ["plaintext"] = plaintext
        };

        if (theirPubkey != null)
            parameters["their_pubkey"] = theirPubkey;

        var result = await CallAsync<Dictionary<string, object?>>("hsm_encrypt_bie1", parameters, cancellationToken: cancellationToken);
        return result.TryGetValue("encrypted", out var enc) && enc is byte[] encBytes ? encBytes : Array.Empty<byte>();
    }

    /// <summary>
    /// Decrypt data using BIE1 ECIES (NBitcoin compatible).
    /// </summary>
    /// <param name="network">Network ("mainnet" or "testnet").</param>
    /// <param name="index">Key index (non-hardened).</param>
    /// <param name="encrypted">BIE1 encrypted blob.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The decrypted plaintext.</returns>
    public async Task<byte[]> HsmDecryptBie1Async(
        string network,
        uint index,
        byte[] encrypted,
        CancellationToken cancellationToken = default)
    {
        if (encrypted.Length < 85)
            throw new ArgumentException("Encrypted data too short for BIE1 format", nameof(encrypted));

        var parameters = new Dictionary<string, object>
        {
            ["network"] = network,
            ["index"] = index,
            ["encrypted"] = encrypted
        };

        var result = await CallAsync<Dictionary<string, object?>>("hsm_decrypt_bie1", parameters, cancellationToken: cancellationToken);
        return result.TryGetValue("plaintext", out var pt) && pt is byte[] ptBytes ? ptBytes : Array.Empty<byte>();
    }

    /// <summary>
    /// Sign a 32-byte hash with compact ECDSA signature (65 bytes: recid+27+4 || R || S).
    /// </summary>
    /// <param name="network">Network ("mainnet" or "testnet").</param>
    /// <param name="index">Key index (non-hardened).</param>
    /// <param name="hash">32-byte hash to sign.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>65-byte compact ECDSA signature.</returns>
    public async Task<byte[]> HsmSignCompactAsync(
        string network,
        uint index,
        byte[] hash,
        CancellationToken cancellationToken = default)
    {
        if (hash.Length != 32)
            throw new ArgumentException("Hash must be 32 bytes", nameof(hash));

        var parameters = new Dictionary<string, object>
        {
            ["network"] = network,
            ["index"] = index,
            ["hash"] = hash
        };

        var result = await CallAsync<Dictionary<string, object?>>("hsm_sign_compact", parameters, cancellationToken: cancellationToken);
        return result.TryGetValue("signature", out var sig) && sig is byte[] sigBytes ? sigBytes : Array.Empty<byte>();
    }

    private static HsmInfo ParseHsmInfo(Dictionary<string, object?> result)
    {
        var info = new HsmInfo
        {
            Active = result.TryGetValue("active", out var active) && active is bool a && a
        };

        if (info.Active)
        {
            if (result.TryGetValue("networks", out var networks) && networks is List<object> netList)
            {
                info.Networks = netList.Select(n => n?.ToString() ?? "").ToArray();
            }

            info.MainnetRootPath = result.TryGetValue("mainnet_root_path", out var mp) ? mp?.ToString() : null;
            info.TestnetRootPath = result.TryGetValue("testnet_root_path", out var tp) ? tp?.ToString() : null;

            if (result.TryGetValue("mainnet_root_pubkey", out var mpk) && mpk is byte[] mainnetPk)
                info.MainnetRootPubkey = mainnetPk;
            if (result.TryGetValue("testnet_root_pubkey", out var tpk) && tpk is byte[] testnetPk)
                info.TestnetRootPubkey = testnetPk;

            if (result.TryGetValue("operations_count", out var ops) && ops != null)
                info.OperationsCount = Convert.ToUInt64(ops);

            if (result.TryGetValue("auto_lock_timeout", out var timeout) && timeout != null)
                info.AutoLockTimeout = Convert.ToUInt32(timeout);

            if (result.TryGetValue("auto_lock_remaining", out var remaining) && remaining != null)
                info.AutoLockRemaining = Convert.ToUInt32(remaining);
        }

        return info;
    }

    #endregion

    /// <summary>
    /// Extract the endpoint path from a full URL.
    /// </summary>
    private static string ExtractEndpoint(string url)
    {
        if (string.IsNullOrEmpty(url))
            return "/";

        try
        {
            var uri = new Uri(url);
            return uri.AbsolutePath;
        }
        catch
        {
            // If URL parsing fails, try to extract path manually
            var pathStart = url.IndexOf('/', url.IndexOf("://") + 3);
            if (pathStart >= 0)
            {
                return url[pathStart..];
            }
            return "/";
        }
    }

    /// <summary>
    /// Drain any pending data from the transport buffer.
    /// </summary>
    public void Drain()
    {
        _transport.Drain();
    }

    private string GenerateRequestId()
    {
        return Interlocked.Increment(ref _requestId).ToString();
    }

    private static T ConvertResult<T>(object? result)
    {
        if (result == null)
        {
            if (default(T) == null)
                return default!;
            throw new JadeException($"Expected result of type {typeof(T).Name}, got null");
        }

        if (result is T typed)
            return typed;

        // Handle common conversions
        var targetType = typeof(T);

        if (targetType == typeof(bool) && result is bool b)
            return (T)(object)b;

        if (targetType == typeof(string) && result is string s)
            return (T)(object)s;

        if (targetType == typeof(byte[]) && result is byte[] bytes)
            return (T)(object)bytes;

        if (targetType == typeof(int) && result is int i)
            return (T)(object)i;

        if (targetType == typeof(long) && result is long l)
            return (T)(object)l;

        if (targetType == typeof(Dictionary<string, object?>) && result is Dictionary<string, object?> dict)
            return (T)(object)dict;

        // Try to convert numeric types
        if (IsNumericType(targetType) && IsNumericValue(result))
        {
            return (T)Convert.ChangeType(result, targetType);
        }

        throw new JadeException($"Cannot convert result from {result.GetType().Name} to {typeof(T).Name}");
    }

    private static bool IsNumericType(Type type)
    {
        return type == typeof(int) || type == typeof(long) || type == typeof(uint) ||
               type == typeof(ulong) || type == typeof(float) || type == typeof(double);
    }

    private static bool IsNumericValue(object value)
    {
        return value is int || value is long || value is uint ||
               value is ulong || value is float || value is double;
    }

    private static VersionInfo ParseVersionInfo(Dictionary<string, object?> result)
    {
        var info = new VersionInfo();

        if (result.TryGetValue("JADE_VERSION", out var version))
            info.JadeVersion = version?.ToString() ?? string.Empty;

        if (result.TryGetValue("JADE_OTA_MAX_CHUNK", out var otaMaxChunk) && otaMaxChunk != null)
            info.OtaMaxChunk = Convert.ToInt32(otaMaxChunk);

        if (result.TryGetValue("JADE_CONFIG", out var config))
            info.Config = config?.ToString() ?? string.Empty;

        if (result.TryGetValue("BOARD_TYPE", out var boardType))
            info.BoardType = boardType?.ToString() ?? string.Empty;

        if (result.TryGetValue("JADE_FEATURES", out var features))
            info.Features = features?.ToString() ?? string.Empty;

        if (result.TryGetValue("EFUSEMAC", out var efuseMac))
            info.EfuseMac = efuseMac?.ToString() ?? string.Empty;

        if (result.TryGetValue("JADE_STATE", out var state))
            info.State = ParseJadeState(state?.ToString());

        if (result.TryGetValue("JADE_NETWORKS", out var networks))
            info.Networks = networks?.ToString() ?? string.Empty;

        if (result.TryGetValue("JADE_HAS_PIN", out var hasPin))
            info.HasPin = hasPin is bool b && b;

        return info;
    }

    private static JadeState ParseJadeState(string? state)
    {
        return state?.ToUpperInvariant() switch
        {
            "READY" => JadeState.Ready,
            "LOCKED" => JadeState.Locked,
            "TEMP" => JadeState.Temp,
            "UNINIT" => JadeState.Uninit,
            _ => JadeState.Uninit
        };
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(JadeRpc));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_ownsTransport)
        {
            _transport.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
