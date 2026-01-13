namespace JadeClient.Models;

/// <summary>
/// HSM mode status and information.
/// </summary>
public class HsmInfo
{
    /// <summary>
    /// Whether HSM mode is currently active.
    /// </summary>
    public bool Active { get; set; }

    /// <summary>
    /// Supported networks when active (e.g., ["mainnet", "testnet"]).
    /// </summary>
    public string[] Networks { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Mainnet root derivation path (e.g., "m/86'/0'/0'/6000'").
    /// </summary>
    public string? MainnetRootPath { get; set; }

    /// <summary>
    /// Testnet root derivation path (e.g., "m/86'/1'/0'/6000'").
    /// </summary>
    public string? TestnetRootPath { get; set; }

    /// <summary>
    /// Mainnet root public key (33 bytes, compressed).
    /// </summary>
    public byte[]? MainnetRootPubkey { get; set; }

    /// <summary>
    /// Testnet root public key (33 bytes, compressed).
    /// </summary>
    public byte[]? TestnetRootPubkey { get; set; }

    /// <summary>
    /// Total number of cryptographic operations performed.
    /// </summary>
    public ulong OperationsCount { get; set; }

    /// <summary>
    /// Auto-lock timeout in seconds (0 = disabled).
    /// </summary>
    public uint AutoLockTimeout { get; set; }

    /// <summary>
    /// Remaining time before auto-lock in seconds (null if disabled).
    /// </summary>
    public uint? AutoLockRemaining { get; set; }
}

/// <summary>
/// Result of HSM public key retrieval.
/// </summary>
public class HsmPubkeyResult
{
    /// <summary>
    /// The public key (33 bytes, compressed secp256k1).
    /// </summary>
    public byte[] Pubkey { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Full derivation path (e.g., "m/86'/0'/0'/6000'/0").
    /// </summary>
    public string Path { get; set; } = string.Empty;
}

/// <summary>
/// Result of HSM extended public key retrieval.
/// </summary>
public class HsmXpubResult
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

/// <summary>
/// Result of HSM signing operation.
/// </summary>
public class HsmSignResult
{
    /// <summary>
    /// The signature.
    /// - For Schnorr: 64 bytes (BIP-340 format)
    /// - For ECDSA: DER encoded (up to 72 bytes)
    /// </summary>
    public byte[] Signature { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// The public key that signed (33 bytes, compressed).
    /// </summary>
    public byte[] Pubkey { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// The algorithm used ("schnorr" or "ecdsa").
    /// </summary>
    public string Algorithm { get; set; } = "schnorr";
}

/// <summary>
/// Result of HSM ECIES encryption.
/// </summary>
public class HsmEncryptResult
{
    /// <summary>
    /// The encrypted ciphertext.
    /// </summary>
    public byte[] Ciphertext { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// The AES-GCM nonce (12 bytes).
    /// </summary>
    public byte[] Nonce { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// The AES-GCM authentication tag (16 bytes).
    /// </summary>
    public byte[] Tag { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// The ephemeral public key used for ECDH (33 bytes).
    /// </summary>
    public byte[] EphemeralPubkey { get; set; } = Array.Empty<byte>();
}
