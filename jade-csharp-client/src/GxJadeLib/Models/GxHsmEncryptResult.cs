namespace GxJadeLib.Models;

/// <summary>
/// Result of HSM ECIES encryption.
/// GeneXus-compatible wrapper for JadeClient.Models.HsmEncryptResult.
/// </summary>
public class GxHsmEncryptResult
{
    /// <summary>
    /// The encrypted ciphertext (hex string).
    /// </summary>
    public string Ciphertext { get; set; } = string.Empty;

    /// <summary>
    /// The AES-GCM nonce (hex string, 12 bytes).
    /// </summary>
    public string Nonce { get; set; } = string.Empty;

    /// <summary>
    /// The AES-GCM authentication tag (hex string, 16 bytes).
    /// </summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>
    /// The ephemeral public key used for ECDH (hex string, 33 bytes).
    /// </summary>
    public string EphemeralPubkey { get; set; } = string.Empty;
}
