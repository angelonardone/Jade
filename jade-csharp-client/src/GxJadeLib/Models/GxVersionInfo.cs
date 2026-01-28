namespace GxJadeLib.Models;

/// <summary>
/// Device version and status information.
/// GeneXus-compatible wrapper for JadeClient.Models.VersionInfo.
/// </summary>
public class GxVersionInfo
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
    /// Current device state as string ("Uninit", "Locked", "Ready", "Temp").
    /// </summary>
    public string State { get; set; } = string.Empty;

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
    public bool HasWallet { get; set; }

    /// <summary>
    /// Whether device is currently unlocked.
    /// </summary>
    public bool IsUnlocked { get; set; }
}
