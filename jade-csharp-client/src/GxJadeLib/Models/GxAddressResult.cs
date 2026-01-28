namespace GxJadeLib.Models;

/// <summary>
/// Result of receive address generation.
/// GeneXus-compatible wrapper.
/// </summary>
public class GxAddressResult
{
    /// <summary>
    /// The generated address.
    /// </summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// The derivation path used (e.g., "m/84'/0'/0'/0/0").
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// The address type variant used:
    /// - "pkh(k)" for Legacy P2PKH (BIP44)
    /// - "sh(wpkh(k))" for Nested SegWit P2SH-P2WPKH (BIP49)
    /// - "wpkh(k)" for Native SegWit P2WPKH (BIP84)
    /// - "tr(k)" for Taproot P2TR (BIP86)
    /// </summary>
    public string Variant { get; set; } = string.Empty;
}
