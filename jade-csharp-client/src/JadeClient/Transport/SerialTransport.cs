using System.IO.Ports;
using System.Runtime.InteropServices;
using JadeClient.Exceptions;
using JadeClient.Models;
using JadeClient.Protocol;

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

                // On Windows with CH9102/ESP32-based devices (like Jade), opening the serial
                // port triggers a device reset regardless of DTR/RTS settings. We need to wait
                // for the device to fully boot before sending commands.
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // Wait for ESP32 bootloader + Jade firmware to initialize
                    // The boot sequence takes approximately 2-3 seconds
                    Thread.Sleep(3500);
                }
                else
                {
                    // On Linux/macOS, the device typically doesn't reset on port open
                    Thread.Sleep(100);
                }

                // Drain any boot messages or pending data
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
    /// <remarks>
    /// Port names vary by operating system:
    /// - Windows: COM1, COM3, COM4, etc.
    /// - macOS: /dev/cu.usbserial-XXXXX, /dev/tty.usbserial-XXXXX
    /// - Linux: /dev/ttyUSB0, /dev/ttyACM0, /dev/ttyS0
    /// </remarks>
    public static string[] GetAvailablePorts()
    {
        return SerialPort.GetPortNames();
    }

    /// <summary>
    /// Gets the current operating system platform.
    /// </summary>
    /// <returns>The detected OS platform.</returns>
    public static OSPlatform GetCurrentPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return OSPlatform.Windows;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return OSPlatform.OSX;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return OSPlatform.Linux;

        // Default to Linux for other Unix-like systems (FreeBSD, etc.)
        return OSPlatform.Linux;
    }

    /// <summary>
    /// Discovers serial ports that are likely to be Jade or similar USB-serial devices.
    /// Filters ports based on the current operating system's naming conventions.
    /// </summary>
    /// <returns>Array of port names that match typical USB-serial device patterns.</returns>
    /// <remarks>
    /// Detection patterns by OS:
    /// - Windows: All COM ports (except COM1 which is usually a legacy port)
    /// - macOS: Ports containing "usbserial", "usbmodem", or "wchusbserial" (CH340/CH9102 chips)
    /// - Linux: Ports starting with "ttyUSB" or "ttyACM" (USB-serial and USB CDC ACM)
    /// </remarks>
    public static string[] DiscoverJadePorts()
    {
        var allPorts = GetAvailablePorts();
        var platform = GetCurrentPlatform();

        if (platform == OSPlatform.Windows)
        {
            // On Windows, most USB-serial devices appear as COM ports
            // Filter out COM1 which is typically a legacy serial port
            return allPorts
                .Where(p => p.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
                .Where(p => !p.Equals("COM1", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p)
                .ToArray();
        }
        else if (platform == OSPlatform.OSX)
        {
            // On macOS, USB-serial devices appear as /dev/cu.* or /dev/tty.*
            // Common patterns:
            // - /dev/cu.usbserial-XXXXX (FTDI, CP210x, etc.)
            // - /dev/cu.usbmodem-XXXXX (CDC ACM devices)
            // - /dev/cu.wchusbserialXXXX (CH340/CH9102 chips)
            // - /dev/cu.SLAB_USBtoUART (Silicon Labs)
            var patterns = new[] { "usbserial", "usbmodem", "wchusbserial", "SLAB_USB" };
            return allPorts
                .Where(p => patterns.Any(pattern => p.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(p => p)
                .ToArray();
        }
        else // Linux and other Unix-like
        {
            // On Linux, USB-serial devices appear as:
            // - /dev/ttyUSB0, /dev/ttyUSB1, etc. (FTDI, CP210x, CH340, etc.)
            // - /dev/ttyACM0, /dev/ttyACM1, etc. (CDC ACM devices like CH9102)
            return allPorts
                .Where(p => p.Contains("ttyUSB") || p.Contains("ttyACM"))
                .OrderBy(p => p)
                .ToArray();
        }
    }

    /// <summary>
    /// Attempts to find the first available Jade device port.
    /// </summary>
    /// <returns>The port name if found, null otherwise.</returns>
    public static string? FindJadePort()
    {
        var jadePorts = DiscoverJadePorts();
        return jadePorts.FirstOrDefault();
    }

    /// <summary>
    /// Probes all candidate serial ports to find and verify a connected Jade device.
    /// This method connects to each port and sends a get_version_info command to verify
    /// that a real Jade device is present.
    /// </summary>
    /// <param name="probeTimeout">Timeout for each port probe (default: 5 seconds).</param>
    /// <param name="progress">Optional progress reporter for status updates.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A JadeProbeResult with the connected transport and device info, or null if no Jade was found.</returns>
    /// <remarks>
    /// The returned transport is already connected and ready to use.
    /// The caller is responsible for disposing the transport when done.
    /// </remarks>
    public static async Task<JadeProbeResult?> FindAndVerifyJadeAsync(
        TimeSpan? probeTimeout = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var timeout = probeTimeout ?? TimeSpan.FromSeconds(5);
        var candidatePorts = DiscoverJadePorts();

        if (candidatePorts.Length == 0)
        {
            // Fall back to all available ports
            candidatePorts = GetAvailablePorts();
        }

        if (candidatePorts.Length == 0)
        {
            progress?.Report("No serial ports detected on this system.");
            return null;
        }

        progress?.Report($"Found {candidatePorts.Length} candidate port(s), probing for Jade device...");

        foreach (var port in candidatePorts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report($"Trying {port}...");

            SerialTransport? testTransport = null;
            JadeRpc? testRpc = null;

            try
            {
                testTransport = new SerialTransport(port);
                await testTransport.ConnectAsync(cancellationToken);

                testRpc = new JadeRpc(testTransport);

                // Try to get version info with a timeout
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeout);

                var versionInfo = await testRpc.GetVersionInfoAsync(cts.Token);

                // Success! We found a real Jade device
                progress?.Report($"Jade found on {port} (v{versionInfo.JadeVersion})");

                // Don't dispose - we're returning these to the caller
                return new JadeProbeResult(testTransport, versionInfo, port);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Timeout on this port - not a Jade or device not responding
                progress?.Report($"{port}: timeout (not a Jade)");
                testRpc?.Dispose();
                testTransport?.Dispose();
            }
            catch (Exception ex)
            {
                // Connection or communication error
                progress?.Report($"{port}: {ex.Message}");
                testRpc?.Dispose();
                testTransport?.Dispose();
            }
        }

        progress?.Report("No Jade device found on any port.");
        return null;
    }

    /// <summary>
    /// Provides information about expected port names for the current operating system.
    /// </summary>
    /// <returns>A description of expected port naming conventions.</returns>
    public static string GetPortNamingHelp()
    {
        var platform = GetCurrentPlatform();

        if (platform == OSPlatform.Windows)
        {
            return "On Windows, Jade devices appear as COM ports (e.g., COM3, COM4).\n" +
                   "Check Device Manager > Ports (COM & LPT) to find your device.";
        }
        else if (platform == OSPlatform.OSX)
        {
            return "On macOS, Jade devices appear as /dev/cu.usbserial-XXXXX or similar.\n" +
                   "Run 'ls /dev/cu.*' in Terminal to list available ports.";
        }
        else
        {
            return "On Linux, Jade devices appear as /dev/ttyUSB0 or /dev/ttyACM0.\n" +
                   "Run 'ls /dev/tty*' in terminal to list available ports.\n" +
                   "You may need to add your user to the 'dialout' group for access.";
        }
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
