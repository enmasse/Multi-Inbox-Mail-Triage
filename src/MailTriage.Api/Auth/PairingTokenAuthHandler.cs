using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MailTriage.Api.Auth;

/// <summary>
/// Custom ASP.NET Core authentication handler for pairing-token bearer auth.
/// Reads the <c>Authorization: Bearer &lt;token&gt;</c> header and validates it
/// against the <see cref="IPairingTokenService"/>.
/// </summary>
public sealed class PairingTokenAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IPairingTokenService _tokenService;

    public PairingTokenAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IPairingTokenService tokenService)
        : base(options, logger, encoder)
    {
        _tokenService = tokenService;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authHeaderValues))
            return Task.FromResult(AuthenticateResult.NoResult());

        var authHeader = authHeaderValues.ToString();
        if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.NoResult());

        var token = authHeader["Bearer ".Length..].Trim();

        if (!_tokenService.ValidateToken(token))
            return Task.FromResult(AuthenticateResult.Fail("Invalid or expired pairing token."));

        var claims = new[] { new Claim(ClaimTypes.Name, "pairing-client") };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
