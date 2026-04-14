using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MailTriage.Api.Services;
using MailTriage.Core.Interfaces;
using MailTriage.Core.Models;

namespace MailTriage.IntegrationTests.Api;

// ---------------------------------------------------------------------------
// Unit tests for PollingStateStore
// ---------------------------------------------------------------------------

public class PollingStateStoreTests
{
    private readonly PollingStateStore _store = new();

    [Fact]
    public void IsRunning_DefaultsFalse()
    {
        _store.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void SetRunning_True_UpdatesIsRunning()
    {
        _store.SetRunning(true);
        _store.IsRunning.Should().BeTrue();
    }

    [Fact]
    public void SetRunning_False_UpdatesIsRunning()
    {
        _store.SetRunning(true);
        _store.SetRunning(false);
        _store.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void RecordPollStart_CreatesEntry()
    {
        _store.RecordPollStart(1, isEnabled: true, mailbox: "INBOX");

        var state = _store.GetAccountState(1);
        state.Should().NotBeNull();
        state!.AccountId.Should().Be(1);
        state.IsEnabled.Should().BeTrue();
        state.Mailbox.Should().Be("INBOX");
        state.LastPollStartedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void RecordPollSuccess_SetsSucceededAndClearsError()
    {
        _store.RecordPollStart(1, true, "INBOX");
        _store.RecordPollFailure(1, "some prior error");
        _store.RecordPollSuccess(1, "msg-123");

        var state = _store.GetAccountState(1);
        state!.LastPollSucceeded.Should().BeTrue();
        state.LastError.Should().BeNull();
        state.LastMessageIdProcessed.Should().Be("msg-123");
        state.LastPollCompletedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void RecordPollFailure_SetsFailedAndError()
    {
        _store.RecordPollStart(2, true, "INBOX");
        _store.RecordPollFailure(2, "Connection refused");

        var state = _store.GetAccountState(2);
        state!.LastPollSucceeded.Should().BeFalse();
        state.LastError.Should().Be("Connection refused");
        state.LastPollCompletedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void GetAllAccountStates_ReturnsAll()
    {
        _store.RecordPollStart(1, true, "INBOX");
        _store.RecordPollStart(2, false, "Sent");

        var all = _store.GetAllAccountStates();
        all.Should().HaveCount(2);
        all.Select(s => s.AccountId).Should().Contain(new[] { 1, 2 });
    }

    [Fact]
    public void GetAccountState_UnknownId_ReturnsNull()
    {
        _store.GetAccountState(999).Should().BeNull();
    }

    [Fact]
    public void GetAllAccountStates_ReturnsCopies_NotMutableReferences()
    {
        _store.RecordPollStart(1, true, "INBOX");
        var snapshots = _store.GetAllAccountStates();

        snapshots.Should().NotBeEmpty();

        // Mutate the returned snapshot; the store must not be affected.
        snapshots[0].Mailbox = "MUTATED";

        var fresh = _store.GetAccountState(1);
        fresh!.Mailbox.Should().Be("INBOX");
    }

    [Fact]
    public void RecordPollSuccess_WithoutMessageId_PreservesExistingLastMessageId()
    {
        _store.RecordPollStart(1, true, "INBOX");
        _store.RecordPollSuccess(1, "first-msg");
        _store.RecordPollSuccess(1, null);  // no new message

        var state = _store.GetAccountState(1);
        state!.LastMessageIdProcessed.Should().Be("first-msg");
    }

    [Fact]
    public async Task Concurrent_Updates_DoNotCorruptState()
    {
        // Hammer the store with concurrent writes from multiple threads.
        var tasks = Enumerable.Range(0, 100).Select(i => Task.Run(() =>
        {
            _store.RecordPollStart(i % 5, true, "INBOX");
            _store.RecordPollSuccess(i % 5, $"msg-{i}");
        })).ToArray();

        await Task.WhenAll(tasks);

        var all = _store.GetAllAccountStates();
        all.Should().HaveCount(5);
        all.Should().AllSatisfy(s => s.LastPollSucceeded.Should().BeTrue());
    }
}

// ---------------------------------------------------------------------------
// Integration tests for /api/status endpoint
// ---------------------------------------------------------------------------

public class StatusIntegrationTests : IDisposable
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public StatusIntegrationTests()
    {
        _factory = new ApiWebApplicationFactory();
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task GetStatus_Returns200WithExpectedFields()
    {
        var response = await _client.GetAsync("/api/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Service metadata
        body.TryGetProperty("startedAt", out _).Should().BeTrue("startedAt must be present");
        body.TryGetProperty("currentTime", out _).Should().BeTrue("currentTime must be present");
        body.TryGetProperty("uptimeSeconds", out var uptime).Should().BeTrue("uptimeSeconds must be present");
        uptime.GetInt64().Should().BeGreaterThanOrEqualTo(0);
        body.TryGetProperty("version", out _).Should().BeTrue("version must be present");

        // Polling section
        body.TryGetProperty("polling", out var polling).Should().BeTrue("polling section must be present");
        polling.TryGetProperty("isRunning", out _).Should().BeTrue("polling.isRunning must be present");
        polling.TryGetProperty("accounts", out var accounts).Should().BeTrue("polling.accounts must be present");
        accounts.ValueKind.Should().Be(JsonValueKind.Array);

        // Dependencies section
        body.TryGetProperty("dependencies", out var deps).Should().BeTrue("dependencies section must be present");
        deps.TryGetProperty("database", out var db).Should().BeTrue("dependencies.database must be present");
        db.TryGetProperty("isReachable", out _).Should().BeTrue("dependencies.database.isReachable must be present");
        deps.TryGetProperty("ollama", out var ollama).Should().BeTrue("dependencies.ollama must be present");
        ollama.TryGetProperty("isReachable", out _).Should().BeTrue("dependencies.ollama.isReachable must be present");
    }

    [Fact]
    public async Task GetStatus_WhenOllamaDown_StillReturns200WithDegradedOllamaStatus()
    {
        // Stop WireMock so Ollama is unreachable
        _factory.WireMock.Stop();

        // Force a refresh so the cached state reflects the stopped server
        var healthService = _factory.Services.GetRequiredService<DependencyHealthService>();
        await healthService.RefreshAsync();

        var response = await _client.GetAsync("/api/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ollamaReachable = body
            .GetProperty("dependencies")
            .GetProperty("ollama")
            .GetProperty("isReachable")
            .GetBoolean();

        ollamaReachable.Should().BeFalse("Ollama should be reported as unreachable when WireMock is stopped");
    }

    [Fact]
    public async Task GetStatus_PollingIsRunning_IsFalse_WhenPollingServiceRemoved()
    {
        // The factory removes MailPollingService, so IsRunning stays false.
        var response = await _client.GetAsync("/api/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("polling").GetProperty("isRunning").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task GetStatus_AfterDirectStateStoreUpdate_ReflectsUpdatedPollingState()
    {
        // Directly update the in-process polling state store to simulate a completed poll.
        var store = _factory.Services.GetRequiredService<IPollingStateStore>();
        store.SetRunning(true);
        store.RecordPollStart(42, isEnabled: true, mailbox: "INBOX");
        store.RecordPollSuccess(42, lastMessageId: "test-msg-id-001");

        var response = await _client.GetAsync("/api/status");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var polling = body.GetProperty("polling");

        polling.GetProperty("isRunning").GetBoolean().Should().BeTrue();

        var accountsArray = polling.GetProperty("accounts");
        accountsArray.GetArrayLength().Should().BeGreaterThanOrEqualTo(1);

        var account = accountsArray.EnumerateArray()
            .FirstOrDefault(a => a.GetProperty("accountId").GetInt32() == 42);
        account.ValueKind.Should().NotBe(JsonValueKind.Undefined, "account 42 should appear in polling.accounts");
        account.GetProperty("lastPollSucceeded").GetBoolean().Should().BeTrue();
        account.GetProperty("lastMessageIdProcessed").GetString().Should().Be("test-msg-id-001");
        account.GetProperty("mailbox").GetString().Should().Be("INBOX");
    }

    [Fact]
    public async Task GetStatus_AfterPollFailure_ReportsLastError()
    {
        var store = _factory.Services.GetRequiredService<IPollingStateStore>();
        store.RecordPollStart(99, isEnabled: true, mailbox: "INBOX");
        store.RecordPollFailure(99, "IMAP authentication failed");

        var response = await _client.GetAsync("/api/status");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var account = body
            .GetProperty("polling")
            .GetProperty("accounts")
            .EnumerateArray()
            .FirstOrDefault(a => a.GetProperty("accountId").GetInt32() == 99);

        account.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        account.GetProperty("lastPollSucceeded").GetBoolean().Should().BeFalse();
        account.GetProperty("lastError").GetString().Should().Be("IMAP authentication failed");
    }
}
