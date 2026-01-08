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
