using HSMbridge.Models;
using JadeClient.Protocol;

namespace HSMbridge.Services;

public class JadeHsmService : IJadeHsmService, IDisposable
{
    private readonly JadeRpc _rpc;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private bool _disposed;
    private string? _deviceVersion;
    private bool _hsmActive;

    public JadeHsmService(JadeRpc rpc)
    {
        _rpc = rpc ?? throw new ArgumentNullException(nameof(rpc));
    }

    public bool IsConnected => _rpc.IsConnected;
    public bool IsHsmActive => _hsmActive;
    public string? DeviceVersion => _deviceVersion;

    public void SetDeviceVersion(string version) => _deviceVersion = version;
    public void SetHsmActive(bool active) => _hsmActive = active;

    public async Task<HsmInfoResponse> GetInfoAsync(CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            var info = await _rpc.HsmGetInfoAsync(ct);
            _hsmActive = info.Active;

            return new HsmInfoResponse
            {
                Active = info.Active,
                Networks = info.Networks,
                MainnetRootPath = info.MainnetRootPath,
                TestnetRootPath = info.TestnetRootPath,
                MainnetRootPubkey = info.MainnetRootPubkey != null ? ToHex(info.MainnetRootPubkey) : null,
                TestnetRootPubkey = info.TestnetRootPubkey != null ? ToHex(info.TestnetRootPubkey) : null,
                OperationsCount = info.OperationsCount,
                AutoLockTimeout = info.AutoLockTimeout,
                AutoLockRemaining = info.AutoLockRemaining
            };
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<HsmPubkeyResponse> GetPubkeyAsync(string network, uint index, CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            var result = await _rpc.HsmGetPubkeyAsync(network, index, ct);
            return new HsmPubkeyResponse
            {
                Pubkey = ToHex(result.Pubkey),
                Path = result.Path
            };
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<HsmXpubResponse> GetXpubAsync(string network, CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            var result = await _rpc.HsmGetXpubAsync(network, ct);
            return new HsmXpubResponse
            {
                Xpub = result.Xpub,
                Path = result.Path
            };
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<HsmSignResponse> SignAsync(HsmSignRequest request, CancellationToken ct = default)
    {
        var hash = FromHex(request.Hash);
        if (hash.Length != 32)
            throw new ArgumentException("Hash must be 32 bytes (64 hex characters)");

        await _semaphore.WaitAsync(ct);
        try
        {
            var result = await _rpc.HsmSignAsync(request.Network, request.Index, hash, request.Algorithm, ct);
            return new HsmSignResponse
            {
                Signature = ToHex(result.Signature),
                Pubkey = ToHex(result.Pubkey),
                Algorithm = result.Algorithm
            };
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<HsmEcdhResponse> EcdhAsync(HsmEcdhRequest request, CancellationToken ct = default)
    {
        var theirPubkey = FromHex(request.TheirPubkey);
        if (theirPubkey.Length != 33 && theirPubkey.Length != 65)
            throw new ArgumentException("Public key must be 33 (compressed) or 65 (uncompressed) bytes");

        await _semaphore.WaitAsync(ct);
        try
        {
            var result = await _rpc.HsmEcdhAsync(request.Network, request.Index, theirPubkey, ct);
            return new HsmEcdhResponse
            {
                SharedSecret = ToHex(result)
            };
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<HsmEncryptResponse> EncryptAsync(HsmEncryptRequest request, CancellationToken ct = default)
    {
        var plaintext = FromHex(request.Plaintext);
        if (plaintext.Length > 1024)
            throw new ArgumentException("Plaintext must not exceed 1024 bytes");

        byte[]? theirPubkey = null;
        if (!string.IsNullOrEmpty(request.TheirPubkey))
        {
            theirPubkey = FromHex(request.TheirPubkey);
            if (theirPubkey.Length != 33 && theirPubkey.Length != 65)
                throw new ArgumentException("Public key must be 33 (compressed) or 65 (uncompressed) bytes");
        }

        byte[]? aad = null;
        if (!string.IsNullOrEmpty(request.Aad))
        {
            aad = FromHex(request.Aad);
        }

        await _semaphore.WaitAsync(ct);
        try
        {
            var result = await _rpc.HsmEncryptAsync(request.Network, request.Index, plaintext, theirPubkey, aad, ct);
            return new HsmEncryptResponse
            {
                Ciphertext = ToHex(result.Ciphertext),
                Nonce = ToHex(result.Nonce),
                Tag = ToHex(result.Tag),
                EphemeralPubkey = ToHex(result.EphemeralPubkey)
            };
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<HsmDecryptResponse> DecryptAsync(HsmDecryptRequest request, CancellationToken ct = default)
    {
        var ciphertext = FromHex(request.Ciphertext);
        var nonce = FromHex(request.Nonce);
        var tag = FromHex(request.Tag);
        var ephemeralPubkey = FromHex(request.EphemeralPubkey);

        if (nonce.Length != 12)
            throw new ArgumentException("Nonce must be 12 bytes");
        if (tag.Length != 16)
            throw new ArgumentException("Tag must be 16 bytes");
        if (ephemeralPubkey.Length != 33)
            throw new ArgumentException("Ephemeral public key must be 33 bytes");

        byte[]? aad = null;
        if (!string.IsNullOrEmpty(request.Aad))
        {
            aad = FromHex(request.Aad);
        }

        await _semaphore.WaitAsync(ct);
        try
        {
            var result = await _rpc.HsmDecryptAsync(
                request.Network, request.Index,
                ciphertext, nonce, tag, ephemeralPubkey,
                aad, ct);
            return new HsmDecryptResponse
            {
                Plaintext = ToHex(result)
            };
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<bool> LockAsync(CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            var result = await _rpc.HsmLockAsync(ct);
            if (result)
                _hsmActive = false;
            return result;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    // BIE1 ECIES encryption - message string -> Base64 encrypted blob
    public async Task<string> EncryptBie1Async(string message, int indexKey, CancellationToken ct = default)
    {
        var plaintext = System.Text.Encoding.UTF8.GetBytes(message);
        if (plaintext.Length > 1024)
            throw new ArgumentException("Message must not exceed 1024 bytes when UTF-8 encoded");

        await _semaphore.WaitAsync(ct);
        try
        {
            var result = await _rpc.HsmEncryptBie1Async("mainnet", (uint)indexKey, plaintext, null, ct);
            return Convert.ToBase64String(result);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    // BIE1 ECIES encryption to specific pubkey - message string -> Base64 encrypted blob
    public async Task<string> EncryptBie1ToPubKeyAsync(string message, string publicKey, CancellationToken ct = default)
    {
        var plaintext = System.Text.Encoding.UTF8.GetBytes(message);
        if (plaintext.Length > 1024)
            throw new ArgumentException("Message must not exceed 1024 bytes when UTF-8 encoded");

        var theirPubkey = FromHex(publicKey);
        if (theirPubkey.Length != 33 && theirPubkey.Length != 65)
            throw new ArgumentException("Public key must be 33 (compressed) or 65 (uncompressed) bytes");

        await _semaphore.WaitAsync(ct);
        try
        {
            // Use index 0 but encrypt to the specified pubkey
            var result = await _rpc.HsmEncryptBie1Async("mainnet", 0, plaintext, theirPubkey, ct);
            return Convert.ToBase64String(result);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    // BIE1 ECIES decryption - Base64 encrypted blob -> plaintext string
    public async Task<string> DecryptBie1Async(string encryptedMessage, int indexKey, CancellationToken ct = default)
    {
        var encrypted = Convert.FromBase64String(encryptedMessage);
        if (encrypted.Length < 85)
            throw new ArgumentException("Encrypted data too short for BIE1 format (minimum 85 bytes)");

        await _semaphore.WaitAsync(ct);
        try
        {
            var plaintext = await _rpc.HsmDecryptBie1Async("mainnet", (uint)indexKey, encrypted, ct);
            return System.Text.Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    // Compact ECDSA signing - hex hash -> hex compact signature (65 bytes)
    public async Task<string> SignCompactAsync(string hashHex, int indexKey, CancellationToken ct = default)
    {
        var hash = FromHex(hashHex);
        if (hash.Length != 32)
            throw new ArgumentException("Hash must be 32 bytes (64 hex characters)");

        await _semaphore.WaitAsync(ct);
        try
        {
            var sig = await _rpc.HsmSignCompactAsync("mainnet", (uint)indexKey, hash, ct);
            return ToHex(sig);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    // Schnorr signing - hex hash -> hex Schnorr signature (64 bytes)
    public async Task<string> SignSchnorrAsync(string hashHex, int indexKey, CancellationToken ct = default)
    {
        var hash = FromHex(hashHex);
        if (hash.Length != 32)
            throw new ArgumentException("Hash must be 32 bytes (64 hex characters)");

        await _semaphore.WaitAsync(ct);
        try
        {
            var result = await _rpc.HsmSignAsync("mainnet", (uint)indexKey, hash, "schnorr", ct);
            return ToHex(result.Signature);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private static string ToHex(byte[] bytes)
    {
        return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }

    private static byte[] FromHex(string hex)
    {
        if (string.IsNullOrEmpty(hex))
            return Array.Empty<byte>();

        hex = hex.Replace(" ", "").Replace("-", "");
        if (hex.Length % 2 != 0)
            throw new ArgumentException("Hex string must have even length");

        byte[] bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }
        return bytes;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _semaphore.Dispose();
            _disposed = true;
        }
    }
}
