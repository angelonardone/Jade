using System.Net.Sockets;
using JadeClient.Exceptions;

namespace JadeClient.Transport;

/// <summary>
/// TCP transport implementation for Jade device communication.
/// Used for connecting to QEMU emulator or remote Jade devices via TCP.
/// </summary>
public class TcpTransport : IJadeTransport
{
    private readonly string _host;
    private readonly int _port;
    private readonly int _connectTimeoutMs;
    private TcpClient? _tcpClient;
    private NetworkStream? _networkStream;
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>
    /// Default TCP port for QEMU emulator.
    /// </summary>
    public const int DefaultQemuPort = 30121;

    /// <summary>
    /// Default connection timeout in milliseconds.
    /// </summary>
    public const int DefaultConnectTimeoutMs = 10000;

    /// <summary>
    /// Creates a new TcpTransport instance.
    /// </summary>
    /// <param name="host">Host address (e.g., "localhost", "127.0.0.1")</param>
    /// <param name="port">TCP port (default: 30121 for QEMU)</param>
    /// <param name="connectTimeoutMs">Connection timeout in milliseconds (default: 10000)</param>
    public TcpTransport(string host, int port = DefaultQemuPort, int connectTimeoutMs = DefaultConnectTimeoutMs)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException("Host cannot be empty", nameof(host));

        if (port <= 0 || port > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 1 and 65535");

        _host = host;
        _port = port;
        _connectTimeoutMs = connectTimeoutMs;
    }

    /// <summary>
    /// Creates a TcpTransport configured for local QEMU emulator.
    /// </summary>
    /// <param name="port">TCP port (default: 30121)</param>
    /// <returns>A new TcpTransport instance.</returns>
    public static TcpTransport CreateForQemu(int port = DefaultQemuPort)
    {
        return new TcpTransport("localhost", port);
    }

    /// <summary>
    /// Creates a TcpTransport from a connection string.
    /// Supports formats: "host:port", "tcp:host:port", or just "host" (uses default port).
    /// </summary>
    /// <param name="connectionString">Connection string (e.g., "localhost:30121", "tcp:192.168.1.100:30121")</param>
    /// <returns>A new TcpTransport instance.</returns>
    public static TcpTransport FromConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string cannot be empty", nameof(connectionString));

        // Remove tcp: prefix if present (for compatibility with Python client format)
        var connStr = connectionString;
        if (connStr.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
            connStr = connStr.Substring(4);

        // Parse host:port
        var parts = connStr.Split(':');
        if (parts.Length == 1)
        {
            return new TcpTransport(parts[0]);
        }
        else if (parts.Length == 2 && int.TryParse(parts[1], out int port))
        {
            return new TcpTransport(parts[0], port);
        }
        else
        {
            throw new ArgumentException($"Invalid connection string format: {connectionString}. Expected 'host:port' or 'tcp:host:port'", nameof(connectionString));
        }
    }

    /// <inheritdoc/>
    public bool IsConnected => _tcpClient?.Connected ?? false;

    /// <inheritdoc/>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        lock (_lock)
        {
            if (IsConnected)
                return;
        }

        try
        {
            _tcpClient = new TcpClient();

            // Connect with timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_connectTimeoutMs);

            await _tcpClient.ConnectAsync(_host, _port, cts.Token);

            _networkStream = _tcpClient.GetStream();

            // Configure socket options for low-latency communication
            _tcpClient.NoDelay = true; // Disable Nagle's algorithm
            _tcpClient.ReceiveTimeout = 0; // No timeout (handled by cancellation)
            _tcpClient.SendTimeout = 30000; // 30 second send timeout
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            CleanupConnection();
            throw new JadeConnectionException($"Connection to {_host}:{_port} timed out after {_connectTimeoutMs}ms");
        }
        catch (OperationCanceledException)
        {
            CleanupConnection();
            throw;
        }
        catch (SocketException ex)
        {
            CleanupConnection();
            throw new JadeConnectionException($"Failed to connect to {_host}:{_port}: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            CleanupConnection();
            throw new JadeConnectionException($"Failed to connect to {_host}:{_port}", ex);
        }
    }

    /// <inheritdoc/>
    public Task DisconnectAsync()
    {
        CleanupConnection();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task WriteAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ThrowIfNotConnected();

        try
        {
            await _networkStream!.WriteAsync(data, 0, data.Length, cancellationToken);
            await _networkStream.FlushAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new JadeConnectionException("Failed to write to TCP connection", ex);
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
                if (_tcpClient!.Available > 0 || ms.Length == 0)
                {
                    int bytesRead = await _networkStream!.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                    if (bytesRead == 0)
                    {
                        throw new JadeConnectionException("Connection closed by remote host");
                    }

                    ms.Write(buffer, 0, bytesRead);

                    // Try to see if we have a complete CBOR message
                    var data = ms.ToArray();
                    if (TryGetCborMessageLength(data, out int expectedLength) && data.Length >= expectedLength)
                    {
                        return data;
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
        catch (JadeConnectionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new JadeConnectionException("Failed to read from TCP connection", ex);
        }
    }

    /// <inheritdoc/>
    public void Drain()
    {
        if (_tcpClient?.Connected == true && _networkStream != null)
        {
            try
            {
                // Read and discard any pending data
                var buffer = new byte[4096];
                while (_tcpClient.Available > 0)
                {
                    _networkStream.Read(buffer, 0, buffer.Length);
                }
            }
            catch
            {
                // Ignore errors during drain
            }
        }
    }

    /// <summary>
    /// Attempts to determine if a complete CBOR message has been received.
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

    private void CleanupConnection()
    {
        lock (_lock)
        {
            if (_networkStream != null)
            {
                try { _networkStream.Close(); } catch { }
                _networkStream = null;
            }

            if (_tcpClient != null)
            {
                try { _tcpClient.Close(); } catch { }
                _tcpClient = null;
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TcpTransport));
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
        CleanupConnection();
        GC.SuppressFinalize(this);
    }
}
