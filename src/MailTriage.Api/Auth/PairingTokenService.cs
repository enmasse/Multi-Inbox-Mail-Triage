using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace MailTriage.Api.Auth;

/// <summary>
/// In-memory pairing-token store. Issues URL-safe bearer tokens and validates them
/// against their expiry timestamps.
/// </summary>
/// <remarks>
/// Registered as a singleton so tokens survive across HTTP requests.
/// On process restart all in-memory tokens are lost; clients must re-provision.
/// </remarks>
public sealed class PairingTokenService : IPairingTokenService
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _tokens = new();
    private readonly PairingTokenOptions _options;

    public PairingTokenService(IOptions<PairingTokenOptions> options)
    {
        _options = options.Value;

        // Pre-seed a bootstrap token if one is configured (never logged).
        if (!string.IsNullOrWhiteSpace(_options.InitialToken))
        {
            _tokens[_options.InitialToken] = DateTimeOffset.UtcNow.Add(_options.TokenExpiry);
        }
    }

    /// <inheritdoc/>
    public string IssueToken(TimeSpan? expiry = null)
    {
        var token = GenerateSecureToken();
        var expiresAt = DateTimeOffset.UtcNow.Add(expiry ?? _options.TokenExpiry);
        _tokens[token] = expiresAt;
        PruneExpired();
        return token;
    }

    /// <inheritdoc/>
    public bool ValidateToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        if (!_tokens.TryGetValue(token, out var expiresAt))
            return false;

        if (DateTimeOffset.UtcNow > expiresAt)
        {
            _tokens.TryRemove(token, out _);
            return false;
        }

        return true;
    }

    private static string GenerateSecureToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        // URL-safe base64 without padding
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private void PruneExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var key in _tokens.Keys)
        {
            if (_tokens.TryGetValue(key, out var exp) && exp < now)
                _tokens.TryRemove(key, out _);
        }
    }
}
