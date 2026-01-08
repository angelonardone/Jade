namespace JadeClient.PinServer;

/// <summary>
/// Interface for handling PIN server requests.
/// Implementations can forward to remote servers or process locally.
/// </summary>
public interface IPinServerHandler : IDisposable
{
    /// <summary>
    /// Process an HTTP request from Jade and return the response.
    /// </summary>
    /// <param name="endpoint">The endpoint path (e.g., "/get_pin", "/set_pin", "/start_handshake").</param>
    /// <param name="requestData">Request payload (format depends on endpoint and protocol version).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response data to send back to Jade.</returns>
    Task<string> ProcessRequestAsync(string endpoint, string? requestData, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the server's public key (33 bytes, compressed secp256k1).
    /// For Remote mode, this returns the remote server's public key.
    /// For Local mode, this returns the local server's public key.
    /// </summary>
    byte[] GetServerPublicKey();
}
