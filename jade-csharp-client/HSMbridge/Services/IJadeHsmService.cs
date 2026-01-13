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
}
