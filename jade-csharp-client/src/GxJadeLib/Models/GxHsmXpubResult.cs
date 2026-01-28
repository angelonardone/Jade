namespace GxJadeLib.Models;

/// <summary>
/// Result of HSM extended public key retrieval.
/// GeneXus-compatible wrapper for JadeClient.Models.HsmXpubResult.
/// </summary>
public class GxHsmXpubResult
{
    /// <summary>
    /// The extended public key (base58-encoded).
    /// </summary>
    public string Xpub { get; set; } = string.Empty;

    /// <summary>
    /// Derivation path (e.g., "m/86'/0'/0'/6000'").
    /// </summary>
    public string Path { get; set; } = string.Empty;
}
