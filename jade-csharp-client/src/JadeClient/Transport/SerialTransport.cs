using System.IO.Ports;
using JadeClient.Exceptions;

namespace JadeClient.Transport;

/// <summary>
/// Serial port transport implementation for Jade device communication.
/// </summary>
public class SerialTransport : IJadeTransport
{
    private readonly string _portName;
    private readonly int _baudRate;
    private SerialPort? _serialPort;
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>
    /// Default serial port configuration for Jade devices.
    /// </summary>
    public const int DefaultBaudRate = 115200;
    public const int DefaultDataBits = 8;
    public const Parity DefaultParity = Parity.None;
    public const StopBits DefaultStopBits = StopBits.One;

    /// <summary>
    /// Creates a new SerialTransport instance.
    /// </summary>
    /// <param name="portName">Serial port name (e.g., "COM3" on Windows, "/dev/cu.usbserial-XXX" on macOS)</param>
    /// <param name="baudRate">Baud rate (default: 115200)</param>
    public SerialTransport(string portName, int baudRate = DefaultBaudRate)
    {
        if (string.IsNullOrWhiteSpace(portName))
            throw new ArgumentException("Port name cannot be empty", nameof(portName));

        _portName = portName;
        _baudRate = baudRate;
    }

    /// <inheritdoc/>
    public bool IsConnected => _serialPort?.IsOpen ?? false;

    /// <inheritdoc/>
    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        lock (_lock)
        {
            if (IsConnected)
                return Task.CompletedTask;

            try
            {
                _serialPort = new SerialPort(_portName, _baudRate, DefaultParity, DefaultDataBits, DefaultStopBits)
                {
                    ReadTimeout = SerialPort.InfiniteTimeout,
                    WriteTimeout = 30000,
                    Handshake = Handshake.None,
                    DtrEnable = true,
                    RtsEnable = true
                };

                _serialPort.Open();

                // Allow device to initialize
                Thread.Sleep(100);

                // Drain any pending data
                Drain();
            }
            catch (Exception ex)
            {
                _serialPort?.Dispose();
                _serialPort = null;
                throw new JadeConnectionException($"Failed to connect to serial port '{_portName}'", ex);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DisconnectAsync()
    {
        lock (_lock)
        {
            if (_serialPort != null)
            {
                if (_serialPort.IsOpen)
                {
                    try
                    {
                        _serialPort.Close();
                    }
                    catch
                    {
                        // Ignore errors during close
                    }
                }
                _serialPort.Dispose();
                _serialPort = null;
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task WriteAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ThrowIfNotConnected();

        try
        {
            await _serialPort!.BaseStream.WriteAsync(data, 0, data.Length, cancellationToken);
            await _serialPort.BaseStream.FlushAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new JadeConnectionException("Failed to write to serial port", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<byte[]> ReadAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ThrowIfNotConnected();

        try
        {
            // Read CBOR data - CBOR is self-delimiting, so we read incrementally
            // and try to parse to detect message boundaries
            using var ms = new MemoryStream();
            var buffer = new byte[4096];

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Check if data is available
                if (_serialPort!.BytesToRead > 0)
                {
                    int bytesRead = await _serialPort.BaseStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                    if (bytesRead > 0)
                    {
                        ms.Write(buffer, 0, bytesRead);

                        // Try to see if we have a complete CBOR message
                        // by attempting to get the expected size
                        var data = ms.ToArray();
                        if (TryGetCborMessageLength(data, out int expectedLength) && data.Length >= expectedLength)
                        {
                            return data;
                        }
                    }
                }
                else
                {
                    // No data available, wait a bit before checking again
                    await Task.Delay(10, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new JadeConnectionException("Failed to read from serial port", ex);
        }
    }

    /// <inheritdoc/>
    public void Drain()
    {
        if (_serialPort?.IsOpen == true)
        {
            try
            {
                _serialPort.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();
            }
            catch
            {
                // Ignore errors during drain
            }
        }
    }

    /// <summary>
    /// Lists available serial ports on the system.
    /// </summary>
    /// <returns>Array of available port names.</returns>
    public static string[] GetAvailablePorts()
    {
        return SerialPort.GetPortNames();
    }

    /// <summary>
    /// Attempts to determine if a complete CBOR message has been received.
    /// This is a simplified check that handles common CBOR map structures.
    /// </summary>
    private static bool TryGetCborMessageLength(byte[] data, out int expectedLength)
    {
        expectedLength = 0;
        if (data.Length == 0)
            return false;

        try
        {
            // Try to decode CBOR to check if message is complete
            // PeterO.Cbor will throw if data is incomplete
            var cbor = PeterO.Cbor.CBORObject.DecodeFromBytes(data);
            expectedLength = data.Length;
            return true;
        }
        catch (PeterO.Cbor.CBORException)
        {
            // Incomplete or invalid CBOR - likely need more data
            return false;
        }
        catch
        {
            return false;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SerialTransport));
    }

    private void ThrowIfNotConnected()
    {
        if (!IsConnected)
            throw new JadeConnectionException("Not connected to device");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        DisconnectAsync().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }
}
