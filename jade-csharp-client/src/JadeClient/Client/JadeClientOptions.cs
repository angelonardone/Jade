namespace JadeClient.Client;

/// <summary>
/// Configuration options for JadeClient.
/// </summary>
public class JadeClientOptions
{
    /// <summary>
    /// Serial port path (e.g., "COM3" on Windows, "/dev/cu.usbserial-XXX" on macOS/Linux).
    /// </summary>
    public string? SerialPort { get; set; }

    /// <summary>
    /// Serial port baud rate. Default is 115200.
    /// </summary>
    public int BaudRate { get; set; } = 115200;

    /// <summary>
    /// Default timeout for RPC operations in milliseconds.
    /// Operations requiring user interaction use extended timeout.
    /// </summary>
    public int DefaultTimeoutMs { get; set; } = 30000;

    /// <summary>
    /// Extended timeout for operations requiring user interaction (PIN entry, tx signing).
    /// Default is 5 minutes.
    /// </summary>
    public int InteractiveTimeoutMs { get; set; } = 300000;

    /// <summary>
    /// Network to use for authentication and signing.
    /// </summary>
    public string Network { get; set; } = "mainnet";

    /// <summary>
    /// Custom HTTP client factory for pinserver requests.
    /// If null, default HttpClient is used.
    /// </summary>
    public Func<HttpClient>? HttpClientFactory { get; set; }

    /// <summary>
    /// Whether to log debug information.
    /// </summary>
    public bool EnableDebugLogging { get; set; } = false;
}

/// <summary>
/// Supported Bitcoin networks.
/// </summary>
public static class Networks
{
    public const string Mainnet = "mainnet";
    public const string Testnet = "testnet";
    public const string Liquid = "liquid";
    public const string LiquidTestnet = "testnet-liquid";
    public const string Regtest = "localtest";
}
