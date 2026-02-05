using HSMbridge.Models;

namespace HSMbridge.Services;

public interface IJadeHsmService
{
    bool IsConnected { get; }
    bool IsHsmActive { get; }
    string? DeviceVersion { get; }

    Task<HsmInfoResponse> GetInfoAsync(CancellationToken ct = default);
    Task<HsmPubkeyResponse> GetPubkeyAsync(string network, uint index, CancellationToken ct = default);
    Task<HsmXpubResponse> GetXpubAsync(string network, CancellationToken ct = default);
    Task<HsmSignResponse> SignAsync(HsmSignRequest request, CancellationToken ct = default);
    Task<HsmEcdhResponse> EcdhAsync(HsmEcdhRequest request, CancellationToken ct = default);
    Task<HsmEncryptResponse> EncryptAsync(HsmEncryptRequest request, CancellationToken ct = default);
    Task<HsmDecryptResponse> DecryptAsync(HsmDecryptRequest request, CancellationToken ct = default);
    Task<bool> LockAsync(CancellationToken ct = default);

    // BIE1 ECIES methods (NBitcoin compatible)
    Task<string> EncryptBie1Async(string message, int indexKey, CancellationToken ct = default);
    Task<string> EncryptBie1ToPubKeyAsync(string message, string publicKey, CancellationToken ct = default);
    Task<string> DecryptBie1Async(string encryptedMessage, int indexKey, CancellationToken ct = default);

    // Compact ECDSA signing (NBitcoin compatible)
    Task<string> SignCompactAsync(string hashHex, int indexKey, CancellationToken ct = default);

    // Schnorr signing
    Task<string> SignSchnorrAsync(string hashHex, int indexKey, CancellationToken ct = default);
}
