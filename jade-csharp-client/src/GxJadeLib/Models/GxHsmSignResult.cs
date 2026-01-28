namespace GxJadeLib.Models;

/// <summary>
/// Result of HSM signing operation.
/// GeneXus-compatible wrapper for JadeClient.Models.HsmSignResult.
/// </summary>
public class GxHsmSignResult
{
    /// <summary>
    /// The signature (hex string).
    /// - For Schnorr: 64 bytes (BIP-340 format)
    /// - For ECDSA: DER encoded (up to 72 bytes)
    /// </summary>
    public string Signature { get; set; } = string.Empty;

    /// <summary>
    /// The public key that signed (hex string, 33 bytes compressed).
    /// </summary>
    public string Pubkey { get; set; } = string.Empty;

    /// <summary>
    /// The algorithm used ("schnorr" or "ecdsa").
    /// </summary>
    public string Algorithm { get; set; } = "schnorr";
}
