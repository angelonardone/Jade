using Microsoft.AspNetCore.Mvc;
using HSMbridge.Models;
using HSMbridge.Services;
using System.Text;
using System.Text.Json.Serialization;

namespace HSMbridge.Controllers;

/// <summary>
/// REST API controller that matches NBitcoin DistributedCryptographyLib API spec.
/// Base URL: /HSM/rest
/// </summary>
[ApiController]
[Route("HSM/rest")]
public class HsmRestController : ControllerBase
{
    private readonly IJadeHsmService _hsmService;
    private readonly ILogger<HsmRestController> _logger;

    public HsmRestController(IJadeHsmService hsmService, ILogger<HsmRestController> logger)
    {
        _hsmService = hsmService;
        _logger = logger;
    }

    /// <summary>
    /// Get public key at the specified index.
    /// </summary>
    /// <param name="Indexkey">Key index (default: 0)</param>
    /// <returns>Public key as hex string</returns>
    [HttpGet("getPubKey")]
    [ProducesResponseType(typeof(GetPubKeyOutput), 200)]
    public async Task<ActionResult<GetPubKeyOutput>> GetPubKey([FromQuery] int? Indexkey = 0, CancellationToken ct = default)
    {
        try
        {
            var index = (uint)(Indexkey ?? 0);
            var result = await _hsmService.GetPubkeyAsync("mainnet", index, ct);
            return Ok(new GetPubKeyOutput
            {
                PublicKey = result.Pubkey,
                Error = ""
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pubkey at index {Index}", Indexkey);
            return Ok(new GetPubKeyOutput
            {
                PublicKey = "",
                Error = ex.Message
            });
        }
    }

    /// <summary>
    /// Encrypt a message using BIE1 ECIES to the key at the specified index.
    /// </summary>
    [HttpPost("encrypt")]
    [ProducesResponseType(typeof(EncryptOutput), 200)]
    public async Task<ActionResult<EncryptOutput>> Encrypt([FromBody] EncryptInput input, CancellationToken ct = default)
    {
        try
        {
            var encryptedBase64 = await _hsmService.EncryptBie1Async(input.Message, input.IndexKey, ct);
            return Ok(new EncryptOutput
            {
                EncryptedMessage = encryptedBase64,
                Error = ""
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error encrypting message");
            return Ok(new EncryptOutput
            {
                EncryptedMessage = "",
                Error = ex.Message
            });
        }
    }

    /// <summary>
    /// Encrypt a message using BIE1 ECIES to a specified public key.
    /// </summary>
    [HttpPost("encryptToPubKey")]
    [ProducesResponseType(typeof(EncryptToPubKeyOutput), 200)]
    public async Task<ActionResult<EncryptToPubKeyOutput>> EncryptToPubKey([FromBody] EncryptToPubKeyInput input, CancellationToken ct = default)
    {
        try
        {
            var encryptedBase64 = await _hsmService.EncryptBie1ToPubKeyAsync(input.Message, input.PublicKey, ct);
            return Ok(new EncryptToPubKeyOutput
            {
                EncryptedMessage = encryptedBase64,
                Error = ""
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error encrypting message to pubkey");
            return Ok(new EncryptToPubKeyOutput
            {
                EncryptedMessage = "",
                Error = ex.Message
            });
        }
    }

    /// <summary>
    /// Decrypt a BIE1 ECIES encrypted message using the key at the specified index.
    /// </summary>
    [HttpPost("decrypt")]
    [ProducesResponseType(typeof(DecryptOutput), 200)]
    public async Task<ActionResult<DecryptOutput>> Decrypt([FromBody] DecryptInput input, CancellationToken ct = default)
    {
        try
        {
            var plaintext = await _hsmService.DecryptBie1Async(input.EncryptedMessage, input.IndexKey, ct);
            return Ok(new DecryptOutput
            {
                Message = plaintext,
                Error = ""
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error decrypting message");
            return Ok(new DecryptOutput
            {
                Message = "",
                Error = ex.Message
            });
        }
    }

    /// <summary>
    /// Sign a hash using compact ECDSA with the key at the specified index.
    /// Input message is a hex-encoded 32-byte hash.
    /// Output is a 65-byte compact signature (recid+27+4 || R || S) as hex.
    /// </summary>
    [HttpPost("sign")]
    [ProducesResponseType(typeof(SignOutput), 200)]
    public async Task<ActionResult<SignOutput>> Sign([FromBody] SignInput input, CancellationToken ct = default)
    {
        try
        {
            var signature = await _hsmService.SignCompactAsync(input.Message, input.IndexKey, ct);
            return Ok(new SignOutput
            {
                Signature = signature,
                Error = ""
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error signing message");
            return Ok(new SignOutput
            {
                Signature = "",
                Error = ex.Message
            });
        }
    }

    /// <summary>
    /// Sign a hash using Schnorr with the key at the specified index.
    /// Input message is a hex-encoded 32-byte hash.
    /// Output is a 64-byte Schnorr signature as hex.
    /// </summary>
    [HttpPost("SignSchnorr")]
    [ProducesResponseType(typeof(SignSchnorrOutput), 200)]
    public async Task<ActionResult<SignSchnorrOutput>> SignSchnorr([FromBody] SignSchnorrInput input, CancellationToken ct = default)
    {
        try
        {
            var signature = await _hsmService.SignSchnorrAsync(input.Message, input.IndexKey, ct);
            return Ok(new SignSchnorrOutput
            {
                Signature = signature,
                Error = ""
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error signing message with Schnorr");
            return Ok(new SignSchnorrOutput
            {
                Signature = "",
                Error = ex.Message
            });
        }
    }
}

// ============== NBitcoin-compatible API Models ==============

public class GetPubKeyOutput
{
    [JsonPropertyName("publicKey")]
    public string PublicKey { get; set; } = "";

    [JsonPropertyName("error")]
    public string Error { get; set; } = "";
}

public class EncryptInput
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("indexKey")]
    public int IndexKey { get; set; }
}

public class EncryptOutput
{
    [JsonPropertyName("encryptedMessage")]
    public string EncryptedMessage { get; set; } = "";

    [JsonPropertyName("error")]
    public string Error { get; set; } = "";
}

public class EncryptToPubKeyInput
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("publicKey")]
    public string PublicKey { get; set; } = "";
}

public class EncryptToPubKeyOutput
{
    [JsonPropertyName("encryptedMessage")]
    public string EncryptedMessage { get; set; } = "";

    [JsonPropertyName("error")]
    public string Error { get; set; } = "";
}

public class DecryptInput
{
    [JsonPropertyName("encryptedMessage")]
    public string EncryptedMessage { get; set; } = "";

    [JsonPropertyName("indexKey")]
    public int IndexKey { get; set; }
}

public class DecryptOutput
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("error")]
    public string Error { get; set; } = "";
}

public class SignInput
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("indexKey")]
    public int IndexKey { get; set; }
}

public class SignOutput
{
    [JsonPropertyName("signature")]
    public string Signature { get; set; } = "";

    [JsonPropertyName("error")]
    public string Error { get; set; } = "";
}

public class SignSchnorrInput
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("indexKey")]
    public int IndexKey { get; set; }
}

public class SignSchnorrOutput
{
    [JsonPropertyName("signature")]
    public string Signature { get; set; } = "";

    [JsonPropertyName("error")]
    public string Error { get; set; } = "";
}
