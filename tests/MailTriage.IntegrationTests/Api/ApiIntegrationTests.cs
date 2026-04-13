using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using MailTriage.Api.BackgroundServices;
using MailTriage.Core.Models;
using MailTriage.Infrastructure.Data;
using MailTriage.Infrastructure.Llm;

namespace MailTriage.IntegrationTests.Api;

/// <summary>
/// Custom WebApplicationFactory that replaces the SQLite database with an isolated in-memory
/// instance and provides a WireMock server for mocking the Ollama API.
/// Each test gets its own factory instance so state never leaks between tests.
/// </summary>
public sealed class ApiWebApplicationFactory : WebApplicationFactory<Program>
{
    // Keep one connection open so the shared-cache in-memory database persists across scopes.
    private readonly Microsoft.Data.Sqlite.SqliteConnection _keepAliveConnection;

    /// <summary>WireMock server standing in for Ollama. Tests configure stubs here.</summary>
    public WireMockServer WireMock { get; } = WireMockServer.Start();

    public ApiWebApplicationFactory()
    {
        var dbName = $"test_{Guid.NewGuid():N}";
        _keepAliveConnection = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={dbName};Mode=Memory;Cache=Shared");
        _keepAliveConnection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Replace any existing DbContextOptions<MailTriageDbContext> so the app
            // connects to our isolated in-memory database rather than the file on disk.
            services.RemoveAll<DbContextOptions<MailTriageDbContext>>();
            services.AddDbContext<MailTriageDbContext>(options =>
                options.UseSqlite(_keepAliveConnection));

            // Point the Ollama HTTP client at WireMock by reconfiguring OllamaOptions.
            services.Configure<OllamaOptions>(o =>
            {
                o.BaseUrl = WireMock.Url!;
                o.TimeoutSeconds = 10;
            });
            // Re-register the HttpClient for OllamaTriageService so it uses the WireMock URL.
            services.RemoveAll<OllamaTriageService>();
            services.AddHttpClient<OllamaTriageService>((sp, client) =>
            {
                var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OllamaOptions>>().Value;
                client.BaseAddress = new Uri(opts.BaseUrl);
            });

            // Remove the background polling service — it tries real IMAP connections.
            var polling = services.SingleOrDefault(
                d => d.ImplementationType == typeof(MailPollingService));
            if (polling != null) services.Remove(polling);
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _keepAliveConnection.Dispose();
            WireMock.Stop();
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// Integration tests for the REST API using WebApplicationFactory.
/// Each test constructs its own factory, giving it a fresh in-memory DB and WireMock.
/// </summary>
public class ApiIntegrationTests : IDisposable
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ApiIntegrationTests()
    {
        _factory = new ApiWebApplicationFactory();
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private void SetupOllamaMock(string category = "FYI", string priority = "Normal")
    {
        var responsePayload = JsonSerializer.Serialize(new
        {
            response = JsonSerializer.Serialize(new
            {
                category,
                priority,
                summary = "Test summary from mock LLM",
                actionRequired = string.Empty,
                labels = Array.Empty<string>()
            })
        });

        _factory.WireMock
            .Given(Request.Create().WithPath("/api/generate").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody(responsePayload)
                .WithHeader("Content-Type", "application/json"));
    }

    [Fact]
    public async Task GetAccounts_ReturnsEmptyListInitially()
    {
        var response = await _client.GetAsync("/api/accounts");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var accounts = await response.Content.ReadFromJsonAsync<JsonElement>();
        accounts.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task PostAccount_CreatesAccount_AndGetReturnsList()
    {
        var payload = new
        {
            name = "Test Gmail",
            host = "imap.gmail.com",
            port = 993,
            username = "test@gmail.com",
            password = "secret",
            useSsl = true,
            mailbox = "INBOX",
            pollingIntervalSeconds = 60
        };

        var createResponse = await _client.PostAsJsonAsync("/api/accounts", payload);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var getResponse = await _client.GetAsync("/api/accounts");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var accounts = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        accounts.GetArrayLength().Should().Be(1);
        accounts[0].GetProperty("name").GetString().Should().Be("Test Gmail");
    }

    [Fact]
    public async Task DeleteAccount_RemovesIt()
    {
        var payload = new
        {
            name = "To Delete",
            host = "imap.test.com",
            port = 993,
            username = "del@test.com",
            password = "pw",
            useSsl = true,
            mailbox = "INBOX",
            pollingIntervalSeconds = 60
        };
        var createResponse = await _client.PostAsJsonAsync("/api/accounts", payload);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetInt32();

        var deleteResponse = await _client.DeleteAsync($"/api/accounts/{id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await _client.GetAsync($"/api/accounts/{id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PostRule_CreatesForwardingRule()
    {
        var rule = new
        {
            name = "Forward Urgent",
            forwardToAddress = "admin@example.com",
            minPriority = (int)TriagePriority.High,
            isEnabled = true
        };

        var response = await _client.PostAsJsonAsync("/api/rules", rule);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var rulesResponse = await _client.GetAsync("/api/rules");
        var rules = await rulesResponse.Content.ReadFromJsonAsync<JsonElement>();
        rules.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ManualTriage_ReturnsTriageResult()
    {
        SetupOllamaMock("Invoice", "High");

        var request = new
        {
            subject = "Invoice #5000 overdue",
            fromAddress = "billing@vendor.com",
            bodyText = "Your invoice is overdue. Please pay immediately."
        };

        var response = await _client.PostAsJsonAsync("/api/triage", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("category").GetInt32().Should().Be((int)TriageCategory.Invoice);
        result.GetProperty("priority").GetInt32().Should().Be((int)TriagePriority.High);
    }

    [Fact]
    public async Task GetEmails_WithQueryFilters_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/emails?category=ActionRequired&minPriority=High");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsHealthy()
    {
        var response = await _client.GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AliveEndpoint_ReturnsHealthy()
    {
        var response = await _client.GetAsync("/alive");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
