namespace MailTriage.Api.Auth;

/// <summary>
/// Configuration options for pairing-token authentication.
/// Bind from the "PairingToken" configuration section.
/// </summary>
public sealed class PairingTokenOptions
{
    /// <summary>
    /// Lifetime of each issued token in hours. Defaults to 24 hours.
    /// </summary>
    public int TokenExpiryHours { get; set; } = 24;

    /// <summary>
    /// When true, the <c>POST /api/pairing/token</c> provisioning endpoint is restricted
    /// to requests originating from localhost. Defaults to true.
    /// Set to false in tests or trusted environments where the network boundary already
    /// limits access.
    /// </summary>
    public bool RequireLocalhostForProvisioning { get; set; } = true;

    /// <summary>
    /// Optional pre-seeded token that is valid on startup. Useful for bootstrap
    /// scenarios (e.g., first run, integration tests). Never log this value.
    /// </summary>
    public string? InitialToken { get; set; }

    /// <summary>Derived helper: token lifetime as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan TokenExpiry => TimeSpan.FromHours(TokenExpiryHours);
}
