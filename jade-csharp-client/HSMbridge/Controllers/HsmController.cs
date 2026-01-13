using Microsoft.AspNetCore.Mvc;
using HSMbridge.Models;
using HSMbridge.Services;

namespace HSMbridge.Controllers;

[ApiController]
[Route("api/hsm")]
public class HsmController : ControllerBase
{
    private readonly IJadeHsmService _hsmService;
    private readonly ILogger<HsmController> _logger;

    public HsmController(IJadeHsmService hsmService, ILogger<HsmController> logger)
    {
        _hsmService = hsmService;
        _logger = logger;
    }

    /// <summary>
    /// Get HSM status and information.
    /// </summary>
    [HttpGet("info")]
    [ProducesResponseType(typeof(HsmInfoResponse), 200)]
    [ProducesResponseType(typeof(ErrorResponse), 500)]
    public async Task<IActionResult> GetInfo(CancellationToken ct)
    {
        try
        {
            var result = await _hsmService.GetInfoAsync(ct);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting HSM info");
            return StatusCode(500, new ErrorResponse { Error = "Failed to get HSM info", Details = ex.Message });
        }
    }

    /// <summary>
    /// Get a public key at a specific index.
    /// </summary>
    [HttpGet("pubkey/{network}/{index}")]
    [ProducesResponseType(typeof(HsmPubkeyResponse), 200)]
    [ProducesResponseType(typeof(ErrorResponse), 400)]
    [ProducesResponseType(typeof(ErrorResponse), 500)]
    public async Task<IActionResult> GetPubkey(string network, uint index, CancellationToken ct)
    {
        try
        {
            var result = await _hsmService.GetPubkeyAsync(network, index, ct);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ErrorResponse { Error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pubkey for {Network}/{Index}", network, index);
            return StatusCode(500, new ErrorResponse { Error = "Failed to get pubkey", Details = ex.Message });
        }
    }

    /// <summary>
    /// Get the extended public key (xpub) for the HSM root.
    /// </summary>
    [HttpGet("xpub/{network}")]
    [ProducesResponseType(typeof(HsmXpubResponse), 200)]
    [ProducesResponseType(typeof(ErrorResponse), 400)]
    [ProducesResponseType(typeof(ErrorResponse), 500)]
    public async Task<IActionResult> GetXpub(string network, CancellationToken ct)
    {
        try
        {
            var result = await _hsmService.GetXpubAsync(network, ct);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ErrorResponse { Error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting xpub for {Network}", network);
            return StatusCode(500, new ErrorResponse { Error = "Failed to get xpub", Details = ex.Message });
        }
    }

    /// <summary>
    /// Sign a 32-byte hash using an HSM key.
    /// </summary>
    [HttpPost("sign")]
    [ProducesResponseType(typeof(HsmSignResponse), 200)]
    [ProducesResponseType(typeof(ErrorResponse), 400)]
    [ProducesResponseType(typeof(ErrorResponse), 500)]
    public async Task<IActionResult> Sign([FromBody] HsmSignRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _hsmService.SignAsync(request, ct);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ErrorResponse { Error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error signing hash");
            return StatusCode(500, new ErrorResponse { Error = "Failed to sign", Details = ex.Message });
        }
    }

    /// <summary>
    /// Compute an ECDH shared secret.
    /// </summary>
    [HttpPost("ecdh")]
    [ProducesResponseType(typeof(HsmEcdhResponse), 200)]
    [ProducesResponseType(typeof(ErrorResponse), 400)]
    [ProducesResponseType(typeof(ErrorResponse), 500)]
    public async Task<IActionResult> Ecdh([FromBody] HsmEcdhRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _hsmService.EcdhAsync(request, ct);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ErrorResponse { Error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error computing ECDH");
            return StatusCode(500, new ErrorResponse { Error = "Failed to compute ECDH", Details = ex.Message });
        }
    }

    /// <summary>
    /// Encrypt data using ECIES.
    /// </summary>
    [HttpPost("encrypt")]
    [ProducesResponseType(typeof(HsmEncryptResponse), 200)]
    [ProducesResponseType(typeof(ErrorResponse), 400)]
    [ProducesResponseType(typeof(ErrorResponse), 500)]
    public async Task<IActionResult> Encrypt([FromBody] HsmEncryptRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _hsmService.EncryptAsync(request, ct);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ErrorResponse { Error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error encrypting");
            return StatusCode(500, new ErrorResponse { Error = "Failed to encrypt", Details = ex.Message });
        }
    }

    /// <summary>
    /// Decrypt data using ECIES.
    /// </summary>
    [HttpPost("decrypt")]
    [ProducesResponseType(typeof(HsmDecryptResponse), 200)]
    [ProducesResponseType(typeof(ErrorResponse), 400)]
    [ProducesResponseType(typeof(ErrorResponse), 500)]
    public async Task<IActionResult> Decrypt([FromBody] HsmDecryptRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _hsmService.DecryptAsync(request, ct);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ErrorResponse { Error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error decrypting");
            return StatusCode(500, new ErrorResponse { Error = "Failed to decrypt", Details = ex.Message });
        }
    }

    /// <summary>
    /// Lock/deactivate HSM mode.
    /// </summary>
    [HttpPost("lock")]
    [ProducesResponseType(typeof(object), 200)]
    [ProducesResponseType(typeof(ErrorResponse), 500)]
    public async Task<IActionResult> Lock(CancellationToken ct)
    {
        try
        {
            var result = await _hsmService.LockAsync(ct);
            return Ok(new { success = result });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error locking HSM");
            return StatusCode(500, new ErrorResponse { Error = "Failed to lock HSM", Details = ex.Message });
        }
    }
}

[ApiController]
[Route("")]
public class HealthController : ControllerBase
{
    private readonly IJadeHsmService _hsmService;

    public HealthController(IJadeHsmService hsmService)
    {
        _hsmService = hsmService;
    }

    /// <summary>
    /// Health check endpoint.
    /// </summary>
    [HttpGet("health")]
    [ProducesResponseType(typeof(HealthResponse), 200)]
    public IActionResult Health()
    {
        return Ok(new HealthResponse
        {
            Healthy = _hsmService.IsConnected && _hsmService.IsHsmActive,
            HsmActive = _hsmService.IsHsmActive,
            DeviceVersion = _hsmService.DeviceVersion
        });
    }
}
