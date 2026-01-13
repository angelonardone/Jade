using System.ComponentModel.DataAnnotations;

namespace HSMbridge.Models;

// ============== Response Models ==============

public class HsmInfoResponse
{
    public bool Active { get; set; }
    public string[] Networks { get; set; } = Array.Empty<string>();
    public string? MainnetRootPath { get; set; }
    public string? TestnetRootPath { get; set; }
    public string? MainnetRootPubkey { get; set; }
    public string? TestnetRootPubkey { get; set; }
    public ulong OperationsCount { get; set; }
    public uint AutoLockTimeout { get; set; }
    public uint? AutoLockRemaining { get; set; }
}

public class HsmPubkeyResponse
{
    public string Pubkey { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}

public class HsmXpubResponse
{
    public string Xpub { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}

public class HsmSignResponse
{
    public string Signature { get; set; } = string.Empty;
    public string Pubkey { get; set; } = string.Empty;
    public string Algorithm { get; set; } = "schnorr";
}

public class HsmEcdhResponse
{
    public string SharedSecret { get; set; } = string.Empty;
}

public class HsmEncryptResponse
{
    public string Ciphertext { get; set; } = string.Empty;
    public string Nonce { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public string EphemeralPubkey { get; set; } = string.Empty;
}

public class HsmDecryptResponse
{
    public string Plaintext { get; set; } = string.Empty;
}

public class HealthResponse
{
    public bool Healthy { get; set; }
    public bool HsmActive { get; set; }
    public string? DeviceVersion { get; set; }
}

public class ErrorResponse
{
    public string Error { get; set; } = string.Empty;
    public string? Details { get; set; }
}

// ============== Request Models ==============

public class HsmSignRequest
{
    [Required]
    public string Network { get; set; } = "mainnet";

    [Required]
    public uint Index { get; set; }

    [Required]
    public string Hash { get; set; } = string.Empty;

    public string Algorithm { get; set; } = "schnorr";
}

public class HsmEcdhRequest
{
    [Required]
    public string Network { get; set; } = "mainnet";

    [Required]
    public uint Index { get; set; }

    [Required]
    public string TheirPubkey { get; set; } = string.Empty;
}

public class HsmEncryptRequest
{
    [Required]
    public string Network { get; set; } = "mainnet";

    [Required]
    public uint Index { get; set; }

    [Required]
    public string Plaintext { get; set; } = string.Empty;

    public string? TheirPubkey { get; set; }

    public string? Aad { get; set; }
}

public class HsmDecryptRequest
{
    [Required]
    public string Network { get; set; } = "mainnet";

    [Required]
    public uint Index { get; set; }

    [Required]
    public string Ciphertext { get; set; } = string.Empty;

    [Required]
    public string Nonce { get; set; } = string.Empty;

    [Required]
    public string Tag { get; set; } = string.Empty;

    [Required]
    public string EphemeralPubkey { get; set; } = string.Empty;

    public string? Aad { get; set; }
}
