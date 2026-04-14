namespace MailTriage.Api.Auth;

/// <summary>
/// Manages pairing tokens: issuing new tokens and validating existing ones.
/// </summary>
public interface IPairingTokenService
{
    /// <summary>Issues a new pairing token with the configured expiry.</summary>
    /// <param name="expiry">Optional custom expiry; defaults to <see cref="PairingTokenOptions.TokenExpiry"/>.</param>
    /// <returns>The opaque bearer token string.</returns>
    string IssueToken(TimeSpan? expiry = null);

    /// <summary>Validates a token, returning true only if it exists and has not expired.</summary>
    bool ValidateToken(string? token);
}
