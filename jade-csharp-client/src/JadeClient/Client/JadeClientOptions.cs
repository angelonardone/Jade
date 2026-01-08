namespace JadeClient.Client;

/// <summary>
/// PIN server mode for authentication.
/// </summary>
public enum PinServerMode
{
    /// <summary>
    /// Use Blockstream's default PIN server (https://j8d.io) or a custom remote server.
    /// </summary>
    Remote,

    /// <summary>
    /// Use local in-process Blind Oracle (self-hosted).
    /// Requires LocalPinServerOptions to be configured.
    /// </summary>
    Local
}

/// <summary>
/// Configuration options for local Blind Oracle PIN server.
/// </summary>
public class LocalPinServerOptions
{
    /// <summary>
    /// Path to server's static private key file (32 bytes).
    /// Will be auto-generated if the file doesn't exist.
    /// </summary>
    public string ServerKeyPath { get; set; } = "./pinserver.key";

    /// <summary>
    /// Storage directory for PIN records.
    /// Each record stored as {sha256(pubkey)}.pin file.
    /// </summary>
    public string StoragePath { get; set; } = "./pins";
}

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
    /// PIN server mode: Remote (Blockstream/custom) or Local (self-hosted Blind Oracle).
    /// Default is Remote.
    /// </summary>
    public PinServerMode PinServerMode { get; set; } = PinServerMode.Remote;

    /// <summary>
    /// For Remote mode: Custom PIN server URL.
    /// If null, uses Blockstream's default server (https://j8d.io).
    /// </summary>
    public string? RemotePinServerUrl { get; set; }

    /// <summary>
    /// For Local mode: Configuration for local Blind Oracle.
    /// Required when PinServerMode is Local.
    /// </summary>
    public LocalPinServerOptions? LocalPinServerOptions { get; set; }

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
