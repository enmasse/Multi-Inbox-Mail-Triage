using System.Net;
using MailTriage.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace MailTriage.Api.Controllers;

/// <summary>
/// Handles pairing-token provisioning. The token endpoint is public (no auth required)
/// but restricted to localhost connections by default to prevent remote token minting.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class PairingController : ControllerBase
{
    private readonly IPairingTokenService _tokenService;
    private readonly PairingTokenOptions _options;

    public PairingController(IPairingTokenService tokenService, IOptions<PairingTokenOptions> options)
    {
        _tokenService = tokenService;
        _options = options.Value;
    }

    /// <summary>
    /// Issues a new pairing bearer token.
    /// In production this endpoint only accepts requests from localhost.
    /// </summary>
    /// <returns>
    /// 200 with <c>{ token, expiresAt }</c> on success.<br/>
    /// 403 when called from a non-localhost address and the restriction is active.
    /// </returns>
    [HttpPost("token")]
    [AllowAnonymous]
    public IActionResult ProvisionToken()
    {
        if (_options.RequireLocalhostForProvisioning && !IsLocalhost())
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new { error = "Token provisioning is only allowed from localhost." });
        }

        var token = _tokenService.IssueToken();
        var expiresAt = DateTimeOffset.UtcNow.Add(_options.TokenExpiry);

        // Return the token; never log it.
        return Ok(new { token, expiresAt });
    }

    private bool IsLocalhost()
    {
        var remoteIp = HttpContext.Connection.RemoteIpAddress;
        if (remoteIp == null)
            return true; // In-process (TestServer) has no remote IP; treat as localhost.
        return IPAddress.IsLoopback(remoteIp);
    }
}
