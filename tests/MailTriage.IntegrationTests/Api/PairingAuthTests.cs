using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace MailTriage.IntegrationTests.Api;

/// <summary>
/// Integration tests that verify end-to-end pairing-token authentication behaviour.
/// Covers: missing token, invalid token, valid provisioned token, health endpoints public.
/// </summary>
public class PairingAuthTests : IDisposable
{
    private readonly ApiWebApplicationFactory _factory;

    public PairingAuthTests()
    {
        _factory = new ApiWebApplicationFactory();
    }

    public void Dispose() => _factory.Dispose();

    // ---------------------------------------------------------------------------
    // Missing token → 401 on protected endpoints
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData("GET",    "/api/accounts")]
    [InlineData("GET",    "/api/rules")]
    [InlineData("GET",    "/api/emails")]
    public async Task MissingToken_ReturnsUnauthorized_OnProtectedGetEndpoints(string method, string path)
    {
        using var client = _factory.CreateClient(); // no Authorization header
        using var request = new HttpRequestMessage(new HttpMethod(method), path);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            because: $"{method} {path} requires a valid pairing token");
    }

    [Fact]
    public async Task MissingToken_ReturnsUnauthorized_OnPostAccounts()
    {
        using var client = _factory.CreateClient();
        var payload = new { name = "X", host = "imap.x.com", port = 993, username = "u", password = "p" };
        var response = await client.PostAsJsonAsync("/api/accounts", payload);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task MissingToken_ReturnsUnauthorized_OnPostRules()
    {
        using var client = _factory.CreateClient();
        var payload = new { name = "R", forwardToAddress = "a@b.com", minPriority = 1, isEnabled = true };
        var response = await client.PostAsJsonAsync("/api/rules", payload);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task MissingToken_ReturnsUnauthorized_OnPostTriage()
    {
        using var client = _factory.CreateClient();
        var payload = new { subject = "S", fromAddress = "a@b.com", bodyText = "body" };
        var response = await client.PostAsJsonAsync("/api/triage", payload);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---------------------------------------------------------------------------
    // Invalid token → 401 on protected endpoints
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData("GET", "/api/accounts")]
    [InlineData("GET", "/api/rules")]
    [InlineData("GET", "/api/emails")]
    public async Task InvalidToken_ReturnsUnauthorized_OnProtectedEndpoints(string method, string path)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "not-a-valid-token-xyz");

        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task MalformedAuthHeader_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Basic dXNlcjpwYXNz"); // Basic, not Bearer

        var response = await client.GetAsync("/api/accounts");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---------------------------------------------------------------------------
    // Health endpoints are public (no token required)
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData("/health")]
    [InlineData("/alive")]
    public async Task HealthEndpoints_AreAccessible_WithoutToken(string path)
    {
        using var client = _factory.CreateClient(); // no auth header
        var response = await client.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            because: $"{path} must remain accessible without authentication");
    }

    // ---------------------------------------------------------------------------
    // Pairing provisioning endpoint is public (no token required)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PairingEndpoint_IsAccessible_WithoutToken()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsync("/api/pairing/token", null);
        // RequireLocalhostForProvisioning=false in test factory → should succeed
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ProvisionToken_ReturnsTokenAndExpiry()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsync("/api/pairing/token", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("token").GetString().Should().NotBeNullOrWhiteSpace();
        body.GetProperty("expiresAt").GetString().Should().NotBeNullOrWhiteSpace();
    }

    // ---------------------------------------------------------------------------
    // Provision token → can call a protected endpoint successfully
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ProvisionToken_ThenCallProtectedEndpoint_ReturnsOk()
    {
        using var client = _factory.CreateClient();

        // Step 1: provision a token (no auth required)
        var provisionResponse = await client.PostAsync("/api/pairing/token", null);
        provisionResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await provisionResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("token").GetString()!;
        token.Should().NotBeNullOrWhiteSpace();

        // Step 2: use the provisioned token to call a protected endpoint
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var protectedResponse = await client.GetAsync("/api/accounts");
        protectedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ProvisionToken_ThenCreateAccount_ReturnsCreated()
    {
        using var client = _factory.CreateClient();

        // Provision token
        var provisionResponse = await client.PostAsync("/api/pairing/token", null);
        var body = await provisionResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("token").GetString()!;

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Create an account using the provisioned token
        var payload = new
        {
            name = "Provisioned-Auth Test",
            host = "imap.example.com",
            port = 993,
            username = "user@example.com",
            password = "secret",
            useSsl = true,
            mailbox = "INBOX",
            pollingIntervalSeconds = 60
        };
        var createResponse = await client.PostAsJsonAsync("/api/accounts", payload);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ---------------------------------------------------------------------------
    // Pre-seeded initial token (bootstrap scenario)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task InitialToken_IsValid_OnStartup()
    {
        // The factory pre-seeds ApiWebApplicationFactory.TestBearerToken as the initial token.
        using var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync("/api/accounts");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
