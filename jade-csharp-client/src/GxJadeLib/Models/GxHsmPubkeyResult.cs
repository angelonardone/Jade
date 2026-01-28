namespace GxJadeLib.Models;

/// <summary>
/// Result of HSM public key retrieval.
/// GeneXus-compatible wrapper for JadeClient.Models.HsmPubkeyResult.
/// </summary>
public class GxHsmPubkeyResult
{
    /// <summary>
    /// The public key (hex string, 33 bytes compressed secp256k1).
    /// </summary>
    public string Pubkey { get; set; } = string.Empty;

    /// <summary>
    /// Full derivation path (e.g., "m/86'/0'/0'/6000'/0").
    /// </summary>
    public string Path { get; set; } = string.Empty;
}
