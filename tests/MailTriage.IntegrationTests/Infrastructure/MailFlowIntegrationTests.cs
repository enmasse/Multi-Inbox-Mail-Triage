using FluentAssertions;
using MailKit.Net.Imap;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text.Json;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using MailTriage.Core.Interfaces;
using MailTriage.Core.Models;
using MailTriage.Infrastructure.Data;
using MailTriage.Infrastructure.Imap;
using MailTriage.Infrastructure.Llm;

namespace MailTriage.IntegrationTests.Infrastructure;

/// <summary>
/// Custom IImapClientFactory for GreenMail tests.
/// GreenMail does not support STARTTLS, so we always use plain-text IMAP (SecureSocketOptions.None).
/// </summary>
internal sealed class NoSslImapClientFactory : IImapClientFactory
{
    public async Task<IImapClient> CreateAndConnectAsync(
        string host, int port, bool useSsl, string username, string password,
        CancellationToken cancellationToken = default)
    {
        var client = new ImapClient();
        await client.ConnectAsync(host, port, SecureSocketOptions.None, cancellationToken);
        await client.AuthenticateAsync(username, password, cancellationToken);
        return client;
    }
}

/// <summary>Test double for IEmailForwarder that discards forwards.</summary>
internal sealed class NoOpEmailForwarder : IEmailForwarder
{
    public Task<bool> ForwardEmailAsync(TriagedEmail email, string toAddress, CancellationToken cancellationToken = default)
        => Task.FromResult(false);
}

/// <summary>Test double for IEmailForwarder that records forwarded emails for assertions.</summary>
internal sealed class TrackingEmailForwarder : IEmailForwarder
{
    public Dictionary<string, List<TriagedEmail>> ForwardedEmails { get; } = new();

    public Task<bool> ForwardEmailAsync(TriagedEmail email, string toAddress, CancellationToken cancellationToken = default)
    {
        if (!ForwardedEmails.ContainsKey(toAddress))
            ForwardedEmails[toAddress] = new List<TriagedEmail>();
        ForwardedEmails[toAddress].Add(email);
        return Task.FromResult(true);
    }
}

[Collection("GreenMail")]
public class MailFlowIntegrationTests : IDisposable
{
    private readonly GreenMailContainerFixture _greenMail;
    private readonly WireMockServer _wireMock;
    private readonly MailTriageDbContext _dbContext;
    private readonly EmailRepository _repository;

    public MailFlowIntegrationTests(GreenMailContainerFixture greenMail)
    {
        _greenMail = greenMail;

        _wireMock = WireMockServer.Start();

        // Use a persistent in-memory SQLite connection so the schema survives across scopes.
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<MailTriageDbContext>()
            .UseSqlite(connection)
            .Options;
        _dbContext = new MailTriageDbContext(options);
        _dbContext.Database.EnsureCreated();
        _repository = new EmailRepository(_dbContext);
    }

    public void Dispose()
    {
        _wireMock.Stop();
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    private void SetupOllamaMock(string category = "FYI", string priority = "Normal", string summary = "Test email summary")
    {
        var responsePayload = JsonSerializer.Serialize(new
        {
            response = JsonSerializer.Serialize(new
            {
                category,
                priority,
                summary,
                actionRequired = string.Empty,
                labels = new[] { "test" }
            })
        });

        _wireMock.Given(
                Request.Create().WithPath("/api/generate").UsingPost())
            .RespondWith(
                Response.Create()
                    .WithStatusCode(HttpStatusCode.OK)
                    .WithBody(responsePayload)
                    .WithHeader("Content-Type", "application/json"));
    }

    private ImapMailMonitorService CreateMonitorService(IEmailForwarder? forwarder = null)
    {
        var httpClient = new HttpClient { BaseAddress = new Uri(_wireMock.Url!) };
        var ollamaOptions = Options.Create(new OllamaOptions
        {
            BaseUrl = _wireMock.Url!,
            Model = "test-model",
            TimeoutSeconds = 30
        });
        var triageService = new OllamaTriageService(httpClient, ollamaOptions, NullLogger<OllamaTriageService>.Instance);

        return new ImapMailMonitorService(
            new NoSslImapClientFactory(),
            _repository,
            triageService,
            forwarder ?? new NoOpEmailForwarder(),
            NullLogger<ImapMailMonitorService>.Instance);
    }

    [Fact]
    public async Task PollAccount_WithRealImap_TriagesAndPersistsEmail()
    {
        SetupOllamaMock("ActionRequired", "High", "Invoice from vendor requires payment");

        var recipientEmail = "testuser@example.com";
        await _greenMail.InjectEmailAsync(
            from: "billing@vendor.com",
            to: recipientEmail,
            subject: "Invoice #1234 - Payment Required",
            body: "Your invoice of $500 is due within 5 days. Please remit payment.");

        await Task.Delay(500);

        var account = await _repository.AddMailAccountAsync(new MailAccount
        {
            Name = "Test GreenMail Account",
            Host = _greenMail.Host,
            Port = _greenMail.ImapPort,
            Username = recipientEmail,
            Password = "password",
            UseSsl = false,
            Mailbox = "INBOX",
            IsEnabled = true
        });

        var service = CreateMonitorService();
        var results = await service.PollAccountAsync(account);

        results.Should().HaveCount(1, "one email was injected");
        var triaged = results[0];
        triaged.Subject.Should().Be("Invoice #1234 - Payment Required");
        triaged.FromAddress.Should().Contain("billing@vendor.com");
        triaged.Category.Should().Be(TriageCategory.ActionRequired);
        triaged.Priority.Should().Be(TriagePriority.High);
        triaged.Summary.Should().NotBeNullOrEmpty();

        var fromDb = await _dbContext.TriagedEmails
            .FirstOrDefaultAsync(e => e.MailAccountId == account.Id);
        fromDb.Should().NotBeNull();
        fromDb!.Subject.Should().Be("Invoice #1234 - Payment Required");
    }

    [Fact]
    public async Task PollAccount_DoesNotReprocessAlreadyTriagedEmail()
    {
        SetupOllamaMock("FYI", "Low", "Newsletter summary");

        var recipientEmail = "deduptest@example.com";
        await _greenMail.InjectEmailAsync(
            from: "newsletter@sender.com",
            to: recipientEmail,
            subject: "Weekly Newsletter",
            body: "Your weekly digest of news.");

        await Task.Delay(500);

        var account = await _repository.AddMailAccountAsync(new MailAccount
        {
            Name = "Dedup Test Account",
            Host = _greenMail.Host,
            Port = _greenMail.ImapPort,
            Username = recipientEmail,
            Password = "password",
            UseSsl = false,
            Mailbox = "INBOX",
            IsEnabled = true
        });

        var service = CreateMonitorService();

        var firstResults = await service.PollAccountAsync(account);
        var secondResults = await service.PollAccountAsync(account);

        firstResults.Should().HaveCount(1, "email processed on first poll");
        secondResults.Should().BeEmpty("email already processed, should be skipped");
    }

    [Fact]
    public async Task PollAccount_WithForwardingRule_ForwardsMatchingEmails()
    {
        SetupOllamaMock("Spam", "Low", "This is a spam email");

        var recipientEmail = "forwardtest@example.com";
        await _greenMail.InjectEmailAsync(
            from: "spammer@bad.com",
            to: recipientEmail,
            subject: "You won a prize!",
            body: "Click here to claim your million dollars.");

        await Task.Delay(500);

        var account = await _repository.AddMailAccountAsync(new MailAccount
        {
            Name = "Forward Test Account",
            Host = _greenMail.Host,
            Port = _greenMail.ImapPort,
            Username = recipientEmail,
            Password = "password",
            UseSsl = false,
            Mailbox = "INBOX",
            IsEnabled = true
        });

        await _repository.AddForwardingRuleAsync(new ForwardingRule
        {
            Name = "Forward Spam",
            MatchCategory = TriageCategory.Spam,
            ForwardToAddress = "spam-reports@example.com",
            IsEnabled = true
        });

        var trackingForwarder = new TrackingEmailForwarder();
        var service = CreateMonitorService(trackingForwarder);
        var results = await service.PollAccountAsync(account);

        results.Should().HaveCount(1);
        results[0].Category.Should().Be(TriageCategory.Spam);
        trackingForwarder.ForwardedEmails.Should().ContainKey("spam-reports@example.com");
    }

    [Fact]
    public async Task PollAccount_WhenOllamaUnavailable_UsesFallbackAndPersists()
    {
        _wireMock.Given(Request.Create().WithPath("/api/generate").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.ServiceUnavailable));

        var recipientEmail = "fallback@example.com";
        await _greenMail.InjectEmailAsync(
            from: "sender@example.com",
            to: recipientEmail,
            subject: "Important email when LLM is down",
            body: "This should still be saved even without LLM triage.");

        await Task.Delay(500);

        var account = await _repository.AddMailAccountAsync(new MailAccount
        {
            Name = "Fallback Test Account",
            Host = _greenMail.Host,
            Port = _greenMail.ImapPort,
            Username = recipientEmail,
            Password = "password",
            UseSsl = false,
            Mailbox = "INBOX",
            IsEnabled = true
        });

        var service = CreateMonitorService();
        var results = await service.PollAccountAsync(account);

        results.Should().HaveCount(1, "email is processed even when LLM fails");
        results[0].Category.Should().Be(TriageCategory.Unknown, "fallback category is Unknown");
        results[0].Priority.Should().Be(TriagePriority.Normal, "fallback priority is Normal");
    }
}
