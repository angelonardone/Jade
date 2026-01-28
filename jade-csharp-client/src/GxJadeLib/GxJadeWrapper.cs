using GxJadeLib.Models;
using JadeClient.Exceptions;
using JadeClient.Models;
using JadeClient.PinServer;
using JadeClient.Protocol;
using JadeClient.Transport;
using JadeClient.Utilities;

namespace GxJadeLib;

/// <summary>
/// Internal connection state holder.
/// </summary>
internal class JadeConnection : IDisposable
{
    public JadeRpc Rpc { get; }
    public SerialTransport Transport { get; }
    public string PortName { get; }
    public DateTime ConnectedAt { get; }

    public JadeConnection(SerialTransport transport, JadeRpc rpc, string portName)
    {
        Transport = transport;
        Rpc = rpc;
        PortName = portName;
        ConnectedAt = DateTime.UtcNow;
    }

    public void Dispose()
    {
        Rpc.Dispose();
        Transport.Dispose();
    }
}

/// <summary>
/// GeneXus External Object wrapper for Jade device communication.
/// Provides synchronous static methods wrapping the async JadeRpc API.
/// All binary data is represented as hexadecimal strings for GeneXus compatibility.
/// </summary>
public static class GxJadeWrapper
{
    private static readonly Dictionary<Guid, JadeConnection> _connections = new();
    private static readonly object _lock = new();

    #region Connection Management

    /// <summary>
    /// Connect to a Jade device on a specific serial port.
    /// </summary>
    /// <param name="portName">Serial port name (e.g., "COM3" on Windows, "/dev/ttyACM0" on Linux).</param>
    /// <returns>JadeOperationResult with ConnectionId on success.</returns>
    public static JadeOperationResult Connect(string portName)
    {
        if (string.IsNullOrWhiteSpace(portName))
            return JadeOperationResult.Fail("Port name cannot be empty");

        try
        {
            var transport = new SerialTransport(portName);
            var rpc = new JadeRpc(transport, ownsTransport: false);

            Task.Run(async () => await rpc.ConnectAsync()).Wait();

            var connectionId = Guid.NewGuid();
            var connection = new JadeConnection(transport, rpc, portName);

            lock (_lock)
            {
                _connections[connectionId] = connection;
            }

            return JadeOperationResult.Ok(connectionId, $"Connected to {portName}");
        }
        catch (Exception ex)
        {
            return HandleException(Guid.Empty, ex);
        }
    }

    /// <summary>
    /// Auto-detect and connect to the first available Jade device.
    /// </summary>
    /// <returns>JadeOperationResult with ConnectionId on success.</returns>
    public static JadeOperationResult ConnectAuto()
    {
        try
        {
            var port = SerialTransport.FindJadePort();
            if (port == null)
            {
                return JadeOperationResult.Fail("No Jade device found. " + SerialTransport.GetPortNamingHelp());
            }

            return Connect(port);
        }
        catch (Exception ex)
        {
            return HandleException(Guid.Empty, ex);
        }
    }

    /// <summary>
    /// Disconnect from a Jade device.
    /// </summary>
    /// <param name="connectionId">The connection ID to disconnect.</param>
    /// <returns>JadeOperationResult indicating success or failure.</returns>
    public static JadeOperationResult Disconnect(Guid connectionId)
    {
        try
        {
            JadeConnection? connection;
            lock (_lock)
            {
                if (!_connections.TryGetValue(connectionId, out connection))
                {
                    return JadeOperationResult.Fail(connectionId, "Connection not found");
                }
                _connections.Remove(connectionId);
            }

            Task.Run(async () => await connection.Rpc.DisconnectAsync()).Wait();
            connection.Dispose();

            return JadeOperationResult.Ok(connectionId, "Disconnected");
        }
        catch (Exception ex)
        {
            return HandleException(connectionId, ex);
        }
    }

    /// <summary>
    /// Check if a connection is still active.
    /// </summary>
    /// <param name="connectionId">The connection ID to check.</param>
    /// <param name="isConnected">Output: true if connected, false otherwise.</param>
    /// <returns>JadeOperationResult indicating success or failure.</returns>
    public static JadeOperationResult IsConnected(Guid connectionId, out bool isConnected)
    {
        isConnected = false;
        try
        {
            var rpc = GetRpc(connectionId);
            if (rpc == null)
            {
                return JadeOperationResult.Fail(connectionId, "Connection not found");
            }

            isConnected = rpc.IsConnected;
            return JadeOperationResult.Ok(connectionId);
        }
        catch (Exception ex)
        {
            return HandleException(connectionId, ex);
        }
    }

    /// <summary>
    /// List available serial ports that may be Jade devices.
    /// </summary>
    /// <param name="ports">Output: comma-separated list of port names.</param>
    /// <returns>JadeOperationResult indicating success or failure.</returns>
    public static JadeOperationResult ListPorts(out string ports)
    {
        ports = string.Empty;
        try
        {
            var jadePorts = SerialTransport.DiscoverJadePorts();
            ports = string.Join(",", jadePorts);
            return JadeOperationResult.Ok(Guid.Empty, $"Found {jadePorts.Length} port(s)");
        }
        catch (Exception ex)
        {
            return HandleException(Guid.Empty, ex);
        }
    }

    /// <summary>
    /// Clear pending data from the connection buffer.
    /// </summary>
    /// <param name="connectionId">The connection ID.</param>
    /// <returns>JadeOperationResult indicating success or failure.</returns>
    public static JadeOperationResult Drain(Guid connectionId)
    {
        try
        {
            var rpc = GetRpc(connectionId);
            if (rpc == null)
            {
                return JadeOperationResult.Fail(connectionId, "Connection not found");
            }

            rpc.Drain();
            return JadeOperationResult.Ok(connectionId);
        }
        catch (Exception ex)
        {
            return HandleException(connectionId, ex);
        }
    }

    #endregion

    #region Device Information

    /// <summary>
    /// Get device version and status information.
    /// </summary>
    /// <param name="connectionId">The connection ID.</param>
    /// <param name="info">Output: device version information.</param>
    /// <returns>JadeOperationResult indicating success or failure.</returns>
    public static JadeOperationResult GetVersionInfo(Guid connectionId, out GxVersionInfo info)
    {
        info = new GxVersionInfo();
        try
        {
            var rpc = GetRpc(connectionId);
            if (rpc == null)
            {
                return JadeOperationResult.Fail(connectionId, "Connection not found");
            }

            VersionInfo? versionInfo = null;
            Task.Run(async () => versionInfo = await rpc.GetVersionInfoAsync()).Wait();

            if (versionInfo != null)
            {
                info.JadeVersion = versionInfo.JadeVersion;
                info.OtaMaxChunk = versionInfo.OtaMaxChunk;
                info.Config = versionInfo.Config;
                info.BoardType = versionInfo.BoardType;
                info.Features = versionInfo.Features;
                info.EfuseMac = versionInfo.EfuseMac;
                info.State = versionInfo.State.ToString();
                info.Networks = versionInfo.Networks;
                info.HasPin = versionInfo.HasPin;
                info.HasWallet = versionInfo.HasWallet;
                info.IsUnlocked = versionInfo.IsUnlocked;
            }

            return JadeOperationResult.Ok(connectionId);
        }
        catch (Exception ex)
        {
            return HandleException(connectionId, ex);
        }
    }

    #endregion

    #region Entropy & Authentication

    /// <summary>
    /// Add entropy to the device RNG.
    /// </summary>
    /// <param name="connectionId">The connection ID.</param>
    /// <param name="entropyHex">Random bytes as hex string.</param>
    /// <returns>JadeOperationResult indicating success or failure.</returns>
    public static JadeOperationResult AddEntropy(Guid connectionId, string entropyHex)
    {
        try
        {
            var rpc = GetRpc(connectionId);
            if (rpc == null)
            {
                return JadeOperationResult.Fail(connectionId, "Connection not found");
            }

            if (!HexConverter.IsValidHex(entropyHex))
            {
                return JadeOperationResult.Fail(connectionId, "Invalid hex string for entropy");
            }

            var entropy = HexConverter.FromHex(entropyHex);
            bool success = false;
            Task.Run(async () => success = await rpc.AddEntropyAsync(entropy)).Wait();

            return success
                ? JadeOperationResult.Ok(connectionId)
                : JadeOperationResult.Fail(connectionId, "Failed to add entropy");
        }
        catch (Exception ex)
        {
            return HandleException(connectionId, ex);
        }
    }

    /// <summary>
    /// Authenticate user with PIN via the remote PIN server.
    /// The user will need to enter their PIN on the device.
    /// </summary>
    /// <param name="connectionId">The connection ID.</param>
    /// <param name="network">Network to authenticate for ("mainnet" or "testnet").</param>
    /// <returns>JadeOperationResult indicating success or failure.</returns>
    public static JadeOperationResult AuthUser(Guid connectionId, string network = "mainnet")
    {
        try
        {
            var rpc = GetRpc(connectionId);
            if (rpc == null)
            {
                return JadeOperationResult.Fail(connectionId, "Connection not found");
            }

            // Use the default remote PIN server
            var pinServerHandler = new RemotePinServerHandler();
            bool success = false;
            Task.Run(async () => success = await rpc.AuthUserAsync(pinServerHandler, network)).Wait();

            return success
                ? JadeOperationResult.Ok(connectionId, "Authentication successful")
                : JadeOperationResult.Fail(connectionId, "Authentication failed");
        }
        catch (Exception ex)
        {
            return HandleException(connectionId, ex);
        }
    }

    /// <summary>
    /// Authenticate user with PIN via a custom PIN server.
    /// </summary>
    /// <param name="connectionId">The connection ID.</param>
    /// <param name="network">Network to authenticate for ("mainnet" or "testnet").</param>
    /// <param name="pinServerUrl">Custom PIN server URL.</param>
    /// <returns>JadeOperationResult indicating success or failure.</returns>
    public static JadeOperationResult AuthUserWithServer(Guid connectionId, string network, string pinServerUrl)
    {
        try
        {
            var rpc = GetRpc(connectionId);
            if (rpc == null)
            {
                return JadeOperationResult.Fail(connectionId, "Connection not found");
            }

            var pinServerHandler = new RemotePinServerHandler(pinServerUrl);
            bool success = false;
            Task.Run(async () => success = await rpc.AuthUserAsync(pinServerHandler, network)).Wait();

            return success
                ? JadeOperationResult.Ok(connectionId, "Authentication successful")
                : JadeOperationResult.Fail(connectionId, "Authentication failed");
        }
        catch (Exception ex)
        {
            return HandleException(connectionId, ex);
        }
    }

    /// <summary>
    /// Logout from the device (lock the wallet).
    /// </summary>
    /// <param name="connectionId">The connection ID.</param>
    /// <returns>JadeOperationResult indicating success or failure.</returns>
    public static JadeOperationResult Logout(Guid connectionId)
    {
        try
        {
            var rpc = GetRpc(connectionId);
            if (rpc == null)
            {
                return JadeOperationResult.Fail(connectionId, "Connection not found");
            }

            bool success = false;
            Task.Run(async () => success = await rpc.LogoutAsync()).Wait();

            return success
                ? JadeOperationResult.Ok(connectionId, "Logged out")
                : JadeOperationResult.Fail(connectionId, "Logout failed");
        }
        catch (Exception ex)
        {
            return HandleException(connectionId, ex);
        }
    }

    #endregion

    #region Key Derivation & Addresses

    /// <summary>
    /// Get an extended public key (xpub) for a derivation path.
    /// </summary>
    /// <param name="connectionId">The connection ID.</param>
    /// <param name="network">Network ("mainnet" or "testnet").</param>
    /// <param name="pathString">BIP32 derivation path (e.g., "m/84'/0'/0'" or "84'/0'/0'").</param>
    /// <param name="result">Output: the xpub result.</param>
    /// <returns>JadeOperationResult indicating success or failure.</returns>
    public static JadeOperationResult GetXpub(Guid connectionId, string network, string pathString, out GxXpubResult result)
    {
        result = new GxXpubResult();
        try
        {
            var rpc = GetRpc(connectionId);
            if (rpc == null)
            {
                return JadeOperationResult.Fail(connectionId, "Connection not found");
            }

            uint[] path = Bip32Path.Parse(pathString);
            string xpub = string.Empty;
            Task.Run(async () => xpub = await rpc.GetXpubAsync(network, path)).Wait();

            result.Xpub = xpub;
            result.Path = Bip32Path.ToString(path);

            return JadeOperationResult.Ok(connectionId);
        }
        catch (Exception ex)
        {
            return HandleException(connectionId, ex);
        }
    }

    /// <summary>
    /// Get a receive address for a given derivation path and address type.
    /// The address will be displayed on the Jade screen for user verification.
    /// </summary>
    /// <param name="connectionId">The connection ID.</param>
    /// <param name="network">Network ("mainnet" or "testnet").</param>
    /// <param name="pathString">Full BIP32 derivation path including address index (e.g., "m/84'/0'/0'/0/0").</param>
    /// <param name="variant">Address type variant:
    /// - "pkh(k)" for Legacy P2PKH (BIP44)
    /// - "sh(wpkh(k))" for Nested SegWit P2SH-P2WPKH (BIP49)
    /// - "wpkh(k)" for Native SegWit P2WPKH (BIP84)
    /// - "tr(k)" for Taproot P2TR (BIP86)</param>
    /// <param name="result">Output: the address result.</param>
    /// <returns>JadeOperationResult indicating success or failure.</returns>
    public static JadeOperationResult GetReceiveAddress(Guid connectionId, string network, string pathString, string variant, out GxAddressResult result)
    {
        result = new GxAddressResult();
        try
        {
            var rpc = GetRpc(connectionId);
            if (rpc == null)
            {
                return JadeOperationResult.Fail(connectionId, "Connection not found");
            }

            uint[] path = Bip32Path.Parse(pathString);
            string address = string.Empty;
            Task.Run(async () => address = await rpc.GetReceiveAddressAsync(network, path, variant)).Wait();

            result.Address = address;
            result.Path = Bip32Path.ToString(path);
            result.Variant = variant;

            return JadeOperationResult.Ok(connectionId);
        }
        catch (Exception ex)
        {
            return HandleException(connectionId, ex);
        }
    }

    #endregion

    #region PIN Server Configuration

    /// <summary>
    /// Update the PIN server configuration on the device.
    /// </summary>
    /// <param name="connectionId">The connection ID.</param>
    /// <param name="urlA">Primary PIN server URL.</param>
    /// <param name="urlB">Secondary PIN server URL (e.g., Tor onion address). Can be empty.</param>
    /// <param name="pubkeyHex">Server's public key (hex string, 33 bytes compressed secp256k1).</param>
    /// <returns>JadeOperationResult indicating success or failure.</returns>
    public static JadeOperationResult UpdatePinServer(Guid connectionId, string urlA, string urlB, string pubkeyHex)
    {
        try
        {
            var rpc = GetRpc(connectionId);
            if (rpc == null)
            {
                return JadeOperationResult.Fail(connectionId, "Connection not found");
            }

            if (!HexConverter.IsValidHex(pubkeyHex))
            {
                return JadeOperationResult.Fail(connectionId, "Invalid hex string for pubkey");
            }

            var pubkey = HexConverter.FromHex(pubkeyHex);
            if (pubkey.Length != 33)
            {
                return JadeOperationResult.Fail(connectionId, "Public key must be 33 bytes");
            }

            bool success = false;
            Task.Run(async () => success = await rpc.UpdatePinServerAsync(
                urlA,
                string.IsNullOrEmpty(urlB) ? null : urlB,
                pubkey)).Wait();

            return success
                ? JadeOperationResult.Ok(connectionId, "PIN server updated")
                : JadeOperationResult.Fail(connectionId, "Failed to update PIN server");
        }
        catch (Exception ex)
        {
            return HandleException(connectionId, ex);
        }
    }

    /// <summary>
    /// Reset the PIN server configuration to Blockstream defaults.
    /// </summary>
    /// <param name="connectionId">The connection ID.</param>
    /// <returns>JadeOperationResult indicating success or failure.</returns>
    public static JadeOperationResult ResetPinServer(Guid connectionId)
    {
        try
        {
            var rpc = GetRpc(connectionId);
            if (rpc == null)
            {
                return JadeOperationResult.Fail(connectionId, "Connection not found");
            }

            bool success = false;
            Task.Run(async () => success = await rpc.ResetPinServerAsync()).Wait();

            return success
                ? JadeOperationResult.Ok(connectionId, "PIN server reset to defaults")
                : JadeOperationResult.Fail(connectionId, "Failed to reset PIN server");
        }
        catch (Exception ex)
        {
            return HandleException(connectionId, ex);
        }
    }

    #endregion

    #region HSM Operations

    /// <summary>
    /// Get HSM mode status and information.
    /// </summary>
    /// <param name="connectionId">The connection ID.</param>
    /// <param name="info">Output: HSM status information.</param>
    /// <returns>JadeOperationResult indicating success or failure.</returns>
    public static JadeOperationResult HsmGetInfo(Guid connectionId, out GxHsmInfo info)
    {
        info = new GxHsmInfo();
        try
        {
            var rpc = GetRpc(connectionId);
            if (rpc == null)
            {
                return JadeOperationResult.Fail(connectionId, "Connection not found");
            }

            HsmInfo? hsmInfo = null;
            Task.Run(async () => hsmInfo = await rpc.HsmGetInfoAsync()).Wait();

            if (hsmInfo != null)
            {
                info.Active = hsmInfo.Active;
                info.Networks = string.Join(",", hsmInfo.Networks);
                info.MainnetRootPath = hsmInfo.MainnetRootPath ?? string.Empty;
                info.TestnetRootPath = hsmInfo.TestnetRootPath ?? string.Empty;
                info.MainnetRootPubkey = HexConverter.ToHex(hsmInfo.MainnetRootPubkey);
                info.TestnetRootPubkey = HexConverter.ToHex(hsmInfo.TestnetRootPubkey);
                info.OperationsCount = hsmInfo.OperationsCount;
                info.AutoLockTimeout = hsmInfo.AutoLockTimeout;
                info.AutoLockRemaining = hsmInfo.AutoLockRemaining ?? 0;
            }

            return JadeOperationResult.Ok(connectionId);
        }
        catch (Exception ex)
        {
            return HandleException(connectionId, ex);
        }
    }

    /// <summary>
    /// Get a public key from the HSM at a specific index.
    /// </summary>
    /// <param name="connectionId">The connection ID.</param>
    /// <param name="network">Network ("mainnet" or "testnet").</param>
    /// <param name="index">Key index (non-hardened, 0 to 2^31-1).</param>
    /// <param name="result">Output: the public key result.</param>
    /// <returns>JadeOperationResult indicating success or failure.</returns>
    public static JadeOperationResult HsmGetPubkey(Guid connectionId, string network, uint index, out GxHsmPubkeyResult result)
    {
        result = new GxHsmPubkeyResult();
        try
        {
            var rpc = GetRpc(connectionId);
            if (rpc == null)
            {
                return JadeOperationResult.Fail(connectionId, "Connection not found");
            }

            HsmPubkeyResult? pubkeyResult = null;
            Task.Run(async () => pubkeyResult = await rpc.HsmGetPubkeyAsync(network, index)).Wait();

            if (pubkeyResult != null)
            {
                result.Pubkey = HexConverter.ToHex(pubkeyResult.Pubkey);
                result.Path = pubkeyResult.Path;
            }

            return JadeOperationResult.Ok(connectionId);
        }
        catch (Exception ex)
        {
            return HandleException(connectionId, ex);
        }
    }

    /// <summary>
    /// Get an extended public key (xpub) for the HSM root key.
    /// </summary>
    /// <param name="connectionId">The connection ID.</param>
    /// <param name="network">Network ("mainnet" or "testnet").</param>
    /// <param name="result">Output: the xpub result.</param>
    /// <returns>JadeOperationResult indicating success or failure.</returns>
    public static JadeOperationResult HsmGetXpub(Guid connectionId, string network, out GxHsmXpubResult result)
    {
        result = new GxHsmXpubResult();
        try
        {
            var rpc = GetRpc(connectionId);
            if (rpc == null)
            {
                return JadeOperationResult.Fail(connectionId, "Connection not found");
            }

            HsmXpubResult? xpubResult = null;
            Task.Run(async () => xpubResult = await rpc.HsmGetXpubAsync(network)).Wait();

            if (xpubResult != null)
            {
                result.Xpub = xpubResult.Xpub;
                result.Path = xpubResult.Path;
            }

            return JadeOperationResult.Ok(connectionId);
        }
        catch (Exception ex)
        {
            return HandleException(connectionId, ex);
        }
    }

    /// <summary>
    /// Sign a 32-byte hash using an HSM key.
    /// </summary>
    /// <param name="connectionId">The connection ID.</param>
    /// <param name="network">Network ("mainnet" or "testnet").</param>
    /// <param name="index">Key index (non-hardened).</param>
    /// <param name="hashHex">32-byte hash to sign (hex string).</param>
    /// <param name="algorithm">Signature algorithm ("schnorr" or "ecdsa"). Defaults to "schnorr".</param>
    /// <param name="result">Output: the signature result.</param>
    /// <returns>JadeOperationResult indicating success or failure.</returns>
    public static JadeOperationResult HsmSign(Guid connectionId, string network, uint index, string hashHex, string algorithm, out GxHsmSignResult result)
    {
        result = new GxHsmSignResult();
        try
        {
            var rpc = GetRpc(connectionId);
            if (rpc == null)
            {
                return JadeOperationResult.Fail(connectionId, "Connection not found");
            }

            if (!HexConverter.IsValidHex(hashHex))
            {
                return JadeOperationResult.Fail(connectionId, "Invalid hex string for hash");
            }

            var hash = HexConverter.FromHex(hashHex);
            if (hash.Length != 32)
            {
                return JadeOperationResult.Fail(connectionId, "Hash must be 32 bytes");
            }

            HsmSignResult? signResult = null;
            Task.Run(async () => signResult = await rpc.HsmSignAsync(network, index, hash, algorithm)).Wait();

            if (signResult != null)
            {
                result.Signature = HexConverter.ToHex(signResult.Signature);
                result.Pubkey = HexConverter.ToHex(signResult.Pubkey);
                result.Algorithm = signResult.Algorithm;
            }

            return JadeOperationResult.Ok(connectionId);
        }
        catch (Exception ex)
        {
            return HandleException(connectionId, ex);
        }
    }

    /// <summary>
    /// Compute an ECDH shared secret using an HSM key.
    /// </summary>
    /// <param name="connectionId">The connection ID.</param>
    /// <param name="network">Network ("mainnet" or "testnet").</param>
    /// <param name="index">Key index (non-hardened).</param>
    /// <param name="theirPubkeyHex">The other party's public key (hex string, 33 or 65 bytes).</param>
    /// <param name="secretHex">Output: the 32-byte shared secret (hex string).</param>
    /// <returns>JadeOperationResult indicating success or failure.</returns>
    public static JadeOperationResult HsmEcdh(Guid connectionId, string network, uint index, string theirPubkeyHex, out string secretHex)
    {
        secretHex = string.Empty;
        try
        {
            var rpc = GetRpc(connectionId);
            if (rpc == null)
            {
                return JadeOperationResult.Fail(connectionId, "Connection not found");
            }

            if (!HexConverter.IsValidHex(theirPubkeyHex))
            {
                return JadeOperationResult.Fail(connectionId, "Invalid hex string for public key");
            }

            var theirPubkey = HexConverter.FromHex(theirPubkeyHex);
            if (theirPubkey.Length != 33 && theirPubkey.Length != 65)
            {
                return JadeOperationResult.Fail(connectionId, "Public key must be 33 (compressed) or 65 (uncompressed) bytes");
            }

            byte[]? secret = null;
            Task.Run(async () => secret = await rpc.HsmEcdhAsync(network, index, theirPubkey)).Wait();

            secretHex = HexConverter.ToHex(secret);

            return JadeOperationResult.Ok(connectionId);
        }
        catch (Exception ex)
        {
            return HandleException(connectionId, ex);
        }
    }

    /// <summary>
    /// Encrypt data using ECIES with an HSM key.
    /// </summary>
    /// <param name="connectionId">The connection ID.</param>
    /// <param name="network">Network ("mainnet" or "testnet").</param>
    /// <param name="index">Key index (non-hardened).</param>
    /// <param name="plaintextHex">Data to encrypt (hex string, max 1024 bytes).</param>
    /// <param name="theirPubkeyHex">Optional recipient public key (hex string). Empty to encrypt to self.</param>
    /// <param name="aadHex">Optional additional authenticated data (hex string).</param>
    /// <param name="result">Output: encryption result with ciphertext, nonce, tag, ephemeral pubkey.</param>
    /// <returns>JadeOperationResult indicating success or failure.</returns>
    public static JadeOperationResult HsmEncrypt(Guid connectionId, string network, uint index, string plaintextHex, string theirPubkeyHex, string aadHex, out GxHsmEncryptResult result)
    {
        result = new GxHsmEncryptResult();
        try
        {
            var rpc = GetRpc(connectionId);
            if (rpc == null)
            {
                return JadeOperationResult.Fail(connectionId, "Connection not found");
            }

            if (!HexConverter.IsValidHex(plaintextHex))
            {
                return JadeOperationResult.Fail(connectionId, "Invalid hex string for plaintext");
            }

            var plaintext = HexConverter.FromHex(plaintextHex);
            if (plaintext.Length > 1024)
            {
                return JadeOperationResult.Fail(connectionId, "Plaintext must not exceed 1024 bytes");
            }

            byte[]? theirPubkey = null;
            if (!string.IsNullOrEmpty(theirPubkeyHex))
            {
                if (!HexConverter.IsValidHex(theirPubkeyHex))
                {
                    return JadeOperationResult.Fail(connectionId, "Invalid hex string for recipient public key");
                }
                theirPubkey = HexConverter.FromHex(theirPubkeyHex);
            }

            byte[]? aad = null;
            if (!string.IsNullOrEmpty(aadHex))
            {
                if (!HexConverter.IsValidHex(aadHex))
                {
                    return JadeOperationResult.Fail(connectionId, "Invalid hex string for AAD");
                }
                aad = HexConverter.FromHex(aadHex);
            }

            HsmEncryptResult? encryptResult = null;
            Task.Run(async () => encryptResult = await rpc.HsmEncryptAsync(network, index, plaintext, theirPubkey, aad)).Wait();

            if (encryptResult != null)
            {
                result.Ciphertext = HexConverter.ToHex(encryptResult.Ciphertext);
                result.Nonce = HexConverter.ToHex(encryptResult.Nonce);
                result.Tag = HexConverter.ToHex(encryptResult.Tag);
                result.EphemeralPubkey = HexConverter.ToHex(encryptResult.EphemeralPubkey);
            }

            return JadeOperationResult.Ok(connectionId);
        }
        catch (Exception ex)
        {
            return HandleException(connectionId, ex);
        }
    }

    /// <summary>
    /// Decrypt data using ECIES with an HSM key.
    /// </summary>
    /// <param name="connectionId">The connection ID.</param>
    /// <param name="network">Network ("mainnet" or "testnet").</param>
    /// <param name="index">Key index (non-hardened).</param>
    /// <param name="ciphertextHex">Encrypted data (hex string).</param>
    /// <param name="nonceHex">12-byte nonce (hex string).</param>
    /// <param name="tagHex">16-byte authentication tag (hex string).</param>
    /// <param name="ephemeralPubkeyHex">33-byte ephemeral public key (hex string).</param>
    /// <param name="aadHex">Optional additional authenticated data (hex string, must match encryption).</param>
    /// <param name="plaintextHex">Output: decrypted plaintext (hex string).</param>
    /// <returns>JadeOperationResult indicating success or failure.</returns>
    public static JadeOperationResult HsmDecrypt(Guid connectionId, string network, uint index, string ciphertextHex, string nonceHex, string tagHex, string ephemeralPubkeyHex, string aadHex, out string plaintextHex)
    {
        plaintextHex = string.Empty;
        try
        {
            var rpc = GetRpc(connectionId);
            if (rpc == null)
            {
                return JadeOperationResult.Fail(connectionId, "Connection not found");
            }

            // Validate all hex inputs
            if (!HexConverter.IsValidHex(ciphertextHex))
                return JadeOperationResult.Fail(connectionId, "Invalid hex string for ciphertext");
            if (!HexConverter.IsValidHex(nonceHex))
                return JadeOperationResult.Fail(connectionId, "Invalid hex string for nonce");
            if (!HexConverter.IsValidHex(tagHex))
                return JadeOperationResult.Fail(connectionId, "Invalid hex string for tag");
            if (!HexConverter.IsValidHex(ephemeralPubkeyHex))
                return JadeOperationResult.Fail(connectionId, "Invalid hex string for ephemeral pubkey");

            var ciphertext = HexConverter.FromHex(ciphertextHex);
            var nonce = HexConverter.FromHex(nonceHex);
            var tag = HexConverter.FromHex(tagHex);
            var ephemeralPubkey = HexConverter.FromHex(ephemeralPubkeyHex);

            if (nonce.Length != 12)
                return JadeOperationResult.Fail(connectionId, "Nonce must be 12 bytes");
            if (tag.Length != 16)
                return JadeOperationResult.Fail(connectionId, "Tag must be 16 bytes");
            if (ephemeralPubkey.Length != 33)
                return JadeOperationResult.Fail(connectionId, "Ephemeral public key must be 33 bytes");

            byte[]? aad = null;
            if (!string.IsNullOrEmpty(aadHex))
            {
                if (!HexConverter.IsValidHex(aadHex))
                    return JadeOperationResult.Fail(connectionId, "Invalid hex string for AAD");
                aad = HexConverter.FromHex(aadHex);
            }

            byte[]? plaintext = null;
            Task.Run(async () => plaintext = await rpc.HsmDecryptAsync(network, index, ciphertext, nonce, tag, ephemeralPubkey, aad)).Wait();

            plaintextHex = HexConverter.ToHex(plaintext);

            return JadeOperationResult.Ok(connectionId);
        }
        catch (Exception ex)
        {
            return HandleException(connectionId, ex);
        }
    }

    /// <summary>
    /// Lock/deactivate HSM mode.
    /// </summary>
    /// <param name="connectionId">The connection ID.</param>
    /// <returns>JadeOperationResult indicating success or failure.</returns>
    public static JadeOperationResult HsmLock(Guid connectionId)
    {
        try
        {
            var rpc = GetRpc(connectionId);
            if (rpc == null)
            {
                return JadeOperationResult.Fail(connectionId, "Connection not found");
            }

            bool success = false;
            Task.Run(async () => success = await rpc.HsmLockAsync()).Wait();

            return success
                ? JadeOperationResult.Ok(connectionId, "HSM locked")
                : JadeOperationResult.Fail(connectionId, "Failed to lock HSM");
        }
        catch (Exception ex)
        {
            return HandleException(connectionId, ex);
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Get the JadeRpc instance for a connection ID.
    /// </summary>
    private static JadeRpc? GetRpc(Guid connectionId)
    {
        lock (_lock)
        {
            return _connections.TryGetValue(connectionId, out var connection) ? connection.Rpc : null;
        }
    }

    /// <summary>
    /// Handle exceptions and convert to JadeOperationResult.
    /// </summary>
    private static JadeOperationResult HandleException(Guid connectionId, Exception ex)
    {
        // Unwrap AggregateException from Task.Wait()
        var innerEx = ex;
        while (innerEx is AggregateException agg && agg.InnerException != null)
        {
            innerEx = agg.InnerException;
        }

        if (innerEx is JadeRpcException rpcEx)
        {
            return JadeOperationResult.Fail(connectionId, rpcEx.Message, rpcEx.ErrorCode);
        }

        if (innerEx is JadeConnectionException connEx)
        {
            return JadeOperationResult.Fail(connectionId, $"Connection error: {connEx.Message}");
        }

        if (innerEx is JadeException jadeEx)
        {
            return JadeOperationResult.Fail(connectionId, jadeEx.Message);
        }

        if (innerEx is TimeoutException timeoutEx)
        {
            return JadeOperationResult.Fail(connectionId, $"Timeout: {timeoutEx.Message}");
        }

        if (innerEx is ArgumentException argEx)
        {
            return JadeOperationResult.Fail(connectionId, $"Invalid argument: {argEx.Message}");
        }

        if (innerEx is FormatException formatEx)
        {
            return JadeOperationResult.Fail(connectionId, $"Format error: {formatEx.Message}");
        }

        return JadeOperationResult.Fail(connectionId, $"Error: {innerEx.Message}");
    }

    /// <summary>
    /// Get the number of active connections (for diagnostics).
    /// </summary>
    public static int GetActiveConnectionCount()
    {
        lock (_lock)
        {
            return _connections.Count;
        }
    }

    /// <summary>
    /// Disconnect all active connections.
    /// </summary>
    public static void DisconnectAll()
    {
        List<Guid> connectionIds;
        lock (_lock)
        {
            connectionIds = _connections.Keys.ToList();
        }

        foreach (var id in connectionIds)
        {
            Disconnect(id);
        }
    }

    #endregion
}
