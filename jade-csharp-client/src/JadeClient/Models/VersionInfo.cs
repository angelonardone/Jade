namespace JadeClient.Models;

/// <summary>
/// Device state enumeration.
/// </summary>
public enum JadeState
{
    /// <summary>Device is uninitialized (no wallet set up).</summary>
    Uninit,
    /// <summary>Device is locked (requires PIN).</summary>
    Locked,
    /// <summary>Device is unlocked and ready.</summary>
    Ready,
    /// <summary>Device is in temporary/ephemeral mode.</summary>
    Temp
}

/// <summary>
/// Version and status information from Jade device.
/// </summary>
public class VersionInfo
{
    /// <summary>
    /// Jade firmware version (e.g., "1.0.38").
    /// </summary>
    public string JadeVersion { get; set; } = string.Empty;

    /// <summary>
    /// Maximum OTA chunk size in bytes.
    /// </summary>
    public int OtaMaxChunk { get; set; }

    /// <summary>
    /// Device configuration (e.g., "BLE", "NORADIO").
    /// </summary>
    public string Config { get; set; } = string.Empty;

    /// <summary>
    /// Board type (e.g., "JADE", "JADE_V1_1", "M5_BLACK_GRAY").
    /// </summary>
    public string BoardType { get; set; } = string.Empty;

    /// <summary>
    /// Feature flags (e.g., "SB" for Secure Boot).
    /// </summary>
    public string Features { get; set; } = string.Empty;

    /// <summary>
    /// Device MAC address (hex string).
    /// </summary>
    public string EfuseMac { get; set; } = string.Empty;

    /// <summary>
    /// Current device state.
    /// </summary>
    public JadeState State { get; set; }

    /// <summary>
    /// Supported networks (e.g., "ALL", "MAIN", "TEST").
    /// </summary>
    public string Networks { get; set; } = string.Empty;

    /// <summary>
    /// Whether device has a PIN set.
    /// </summary>
    public bool HasPin { get; set; }

    /// <summary>
    /// Whether device has a wallet initialized.
    /// </summary>
    public bool HasWallet => State != JadeState.Uninit;

    /// <summary>
    /// Whether device is currently unlocked.
    /// </summary>
    public bool IsUnlocked => State == JadeState.Ready || State == JadeState.Temp;
}
