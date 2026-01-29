namespace GxJadeLib.Models;

/// <summary>
/// HSM mode status and information.
/// GeneXus-compatible wrapper for JadeClient.Models.HsmInfo.
/// </summary>
public class GxHsmInfo
{
    /// <summary>
    /// Whether HSM mode is currently active.
    /// </summary>
    public bool Active { get; set; }

    /// <summary>
    /// Supported networks as comma-separated string (e.g., "mainnet,testnet").
    /// </summary>
    public string Networks { get; set; } = string.Empty;

    /// <summary>
    /// Mainnet root derivation path (e.g., "m/86'/0'/0'/6000'").
    /// </summary>
    public string MainnetRootPath { get; set; } = string.Empty;

    /// <summary>
    /// Testnet root derivation path (e.g., "m/86'/1'/0'/6000'").
    /// </summary>
    public string TestnetRootPath { get; set; } = string.Empty;

    /// <summary>
    /// Mainnet root public key (hex string, 33 bytes compressed).
    /// </summary>
    public string MainnetRootPubkey { get; set; } = string.Empty;

    /// <summary>
    /// Testnet root public key (hex string, 33 bytes compressed).
    /// </summary>
    public string TestnetRootPubkey { get; set; } = string.Empty;

    /// <summary>
    /// Total number of cryptographic operations performed.
    /// </summary>
    public long OperationsCount { get; set; }

    /// <summary>
    /// Auto-lock timeout in seconds (0 = disabled).
    /// </summary>
    public long AutoLockTimeout { get; set; }

    /// <summary>
    /// Remaining time before auto-lock in seconds (0 if disabled or not applicable).
    /// </summary>
    public long AutoLockRemaining { get; set; }
}
