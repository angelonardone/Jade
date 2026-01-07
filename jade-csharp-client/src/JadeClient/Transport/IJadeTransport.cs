namespace JadeClient.Transport;

/// <summary>
/// Interface for Jade device transport layer.
/// Implementations handle the low-level communication (Serial, BLE, TCP).
/// </summary>
public interface IJadeTransport : IDisposable
{
    /// <summary>
    /// Gets whether the transport is currently connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Connect to the Jade device.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnect from the Jade device.
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// Write data to the Jade device.
    /// </summary>
    /// <param name="data">Data bytes to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task WriteAsync(byte[] data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Read data from the Jade device.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Received data bytes.</returns>
    Task<byte[]> ReadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Drain any pending data from the transport buffer.
    /// </summary>
    void Drain();
}
