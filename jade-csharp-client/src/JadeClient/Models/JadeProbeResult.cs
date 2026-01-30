using JadeClient.Transport;

namespace JadeClient.Models;

/// <summary>
/// Result of probing serial ports for a Jade device.
/// Contains the connected transport, device information, and port name.
/// </summary>
public class JadeProbeResult
{
    /// <summary>
    /// The connected serial transport for the found Jade device.
    /// </summary>
    public SerialTransport Transport { get; }

    /// <summary>
    /// Version and status information from the Jade device.
    /// </summary>
    public VersionInfo VersionInfo { get; }

    /// <summary>
    /// The serial port name where the Jade was found (e.g., "COM4", "/dev/cu.usbserial-XXX").
    /// </summary>
    public string PortName { get; }

    /// <summary>
    /// Creates a new JadeProbeResult.
    /// </summary>
    /// <param name="transport">The connected transport.</param>
    /// <param name="versionInfo">Device version information.</param>
    /// <param name="portName">The port name.</param>
    public JadeProbeResult(SerialTransport transport, VersionInfo versionInfo, string portName)
    {
        Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        VersionInfo = versionInfo ?? throw new ArgumentNullException(nameof(versionInfo));
        PortName = portName ?? throw new ArgumentNullException(nameof(portName));
    }
}
