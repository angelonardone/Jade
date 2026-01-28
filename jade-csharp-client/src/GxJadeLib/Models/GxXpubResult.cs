namespace GxJadeLib.Models;

/// <summary>
/// Result of extended public key retrieval.
/// GeneXus-compatible wrapper.
/// </summary>
public class GxXpubResult
{
    /// <summary>
    /// The extended public key (base58-encoded).
    /// </summary>
    public string Xpub { get; set; } = string.Empty;

    /// <summary>
    /// The derivation path used (e.g., "m/84'/0'/0'").
    /// </summary>
    public string Path { get; set; } = string.Empty;
}
