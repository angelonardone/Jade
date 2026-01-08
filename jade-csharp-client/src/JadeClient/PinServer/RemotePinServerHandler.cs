using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JadeClient.Exceptions;

namespace JadeClient.PinServer;

/// <summary>
/// PIN server handler that forwards requests to a remote HTTP server.
/// By default, uses Blockstream's PIN server at https://j8d.io.
/// </summary>
public class RemotePinServerHandler : IPinServerHandler
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly bool _ownsHttpClient;
    private byte[]? _serverPublicKey;
    private bool _disposed;

    /// <summary>
    /// Default Blockstream PIN server URL.
    /// </summary>
    public const string DefaultPinServerUrl = "https://j8d.io";

    /// <summary>
    /// Blockstream's default PIN server public key (hex).
    /// </summary>
    public const string DefaultServerPublicKeyHex = "0325f3c5a0f77b0b7346a13dd8c29f6ea91e4c8e9ed69c2c78717ac4b6ec6c4d33";

    /// <summary>
    /// Creates a new RemotePinServerHandler.
    /// </summary>
    /// <param name="baseUrl">Base URL of the PIN server. If null, uses Blockstream's default.</param>
    /// <param name="httpClient">Optional HttpClient instance. If null, creates a new one.</param>
    public RemotePinServerHandler(string? baseUrl = null, HttpClient? httpClient = null)
    {
        _baseUrl = (baseUrl ?? DefaultPinServerUrl).TrimEnd('/');
        _ownsHttpClient = httpClient == null;
        _httpClient = httpClient ?? new HttpClient();

        // Set default server public key for Blockstream
        if (_baseUrl == DefaultPinServerUrl)
        {
            _serverPublicKey = Convert.FromHexString(DefaultServerPublicKeyHex);
        }
    }

    /// <summary>
    /// Creates a new RemotePinServerHandler with a custom server public key.
    /// </summary>
    /// <param name="baseUrl">Base URL of the PIN server.</param>
    /// <param name="serverPublicKey">Server's public key (33 bytes, compressed).</param>
    /// <param name="httpClient">Optional HttpClient instance.</param>
    public RemotePinServerHandler(string baseUrl, byte[] serverPublicKey, HttpClient? httpClient = null)
        : this(baseUrl, httpClient)
    {
        _serverPublicKey = serverPublicKey ?? throw new ArgumentNullException(nameof(serverPublicKey));
    }

    /// <inheritdoc/>
    public async Task<string> ProcessRequestAsync(string endpoint, string? requestData, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var url = _baseUrl + endpoint;

        try
        {
            HttpResponseMessage response;

            if (string.IsNullOrEmpty(requestData))
            {
                // For endpoints like /start_handshake that may not have data
                var emptyContent = new StringContent("{}", Encoding.UTF8, "application/json");
                response = await _httpClient.PostAsync(url, emptyContent, cancellationToken);
            }
            else
            {
                // The requestData is already JSON from Jade - send it directly
                var content = new StringContent(requestData, Encoding.UTF8, "application/json");
                response = await _httpClient.PostAsync(url, content, cancellationToken);
            }

            response.EnsureSuccessStatusCode();

            // Return the raw JSON response - it will be parsed and passed to Jade
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new JadeException($"Failed to communicate with PIN server at {url}: {ex.Message}", ex);
        }
        catch (JsonException ex)
        {
            throw new JadeException("Failed to parse PIN server response", ex);
        }
    }

    /// <inheritdoc/>
    public byte[] GetServerPublicKey()
    {
        if (_serverPublicKey == null)
        {
            throw new InvalidOperationException(
                "Server public key not set. For custom servers, provide the public key in the constructor.");
        }

        return _serverPublicKey;
    }

    /// <summary>
    /// Sets the server's public key (for custom PIN servers).
    /// </summary>
    /// <param name="publicKey">Server's public key (33 bytes, compressed secp256k1).</param>
    public void SetServerPublicKey(byte[] publicKey)
    {
        if (publicKey == null || publicKey.Length != 33)
        {
            throw new ArgumentException("Public key must be 33 bytes (compressed secp256k1)", nameof(publicKey));
        }

        _serverPublicKey = publicKey;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(RemotePinServerHandler));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
