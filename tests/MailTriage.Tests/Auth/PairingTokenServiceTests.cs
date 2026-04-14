using FluentAssertions;
using MailTriage.Api.Auth;
using Microsoft.Extensions.Options;

namespace MailTriage.Tests.Auth;

/// <summary>
/// Unit tests for <see cref="PairingTokenService"/>.
/// </summary>
public class PairingTokenServiceTests
{
    private static PairingTokenService CreateService(Action<PairingTokenOptions>? configure = null)
    {
        var opts = new PairingTokenOptions();
        configure?.Invoke(opts);
        return new PairingTokenService(Options.Create(opts));
    }

    [Fact]
    public void IssueToken_ReturnsNonEmptyToken()
    {
        var svc = CreateService();
        var token = svc.IssueToken();
        token.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void IssueToken_TokenIsValidImmediately()
    {
        var svc = CreateService();
        var token = svc.IssueToken();
        svc.ValidateToken(token).Should().BeTrue();
    }

    [Fact]
    public void IssueToken_MultipleTokensAreUnique()
    {
        var svc = CreateService();
        var tokens = Enumerable.Range(0, 20).Select(_ => svc.IssueToken()).ToList();
        tokens.Distinct().Should().HaveCount(20);
    }

    [Fact]
    public void ValidateToken_ReturnsFalse_ForUnknownToken()
    {
        var svc = CreateService();
        svc.ValidateToken("not-a-real-token").Should().BeFalse();
    }

    [Fact]
    public void ValidateToken_ReturnsFalse_ForNullToken()
    {
        var svc = CreateService();
        svc.ValidateToken(null).Should().BeFalse();
    }

    [Fact]
    public void ValidateToken_ReturnsFalse_ForEmptyToken()
    {
        var svc = CreateService();
        svc.ValidateToken(string.Empty).Should().BeFalse();
        svc.ValidateToken("   ").Should().BeFalse();
    }

    [Fact]
    public void ValidateToken_ReturnsFalse_ForExpiredToken()
    {
        // Issue a token that expires immediately (negative expiry = already in the past)
        var svc = CreateService();
        var token = svc.IssueToken(expiry: TimeSpan.FromSeconds(-1));
        svc.ValidateToken(token).Should().BeFalse();
    }

    [Fact]
    public void ValidateToken_ReturnsTrue_ForInitialToken()
    {
        const string bootstrapToken = "my-bootstrap-token-xyz";
        var svc = CreateService(opts => opts.InitialToken = bootstrapToken);
        svc.ValidateToken(bootstrapToken).Should().BeTrue();
    }

    [Fact]
    public void ValidateToken_ReturnsFalse_WhenNoInitialTokenConfigured()
    {
        var svc = CreateService(); // no InitialToken
        svc.ValidateToken("whatever").Should().BeFalse();
    }

    [Fact]
    public void TokenExpiry_DefaultsTo24Hours()
    {
        var opts = new PairingTokenOptions();
        opts.TokenExpiry.Should().Be(TimeSpan.FromHours(24));
    }

    [Fact]
    public void TokenExpiry_ReflectsConfiguredHours()
    {
        var opts = new PairingTokenOptions { TokenExpiryHours = 8 };
        opts.TokenExpiry.Should().Be(TimeSpan.FromHours(8));
    }

    [Fact]
    public void RequireLocalhostForProvisioning_DefaultsToTrue()
    {
        var opts = new PairingTokenOptions();
        opts.RequireLocalhostForProvisioning.Should().BeTrue();
    }
}
