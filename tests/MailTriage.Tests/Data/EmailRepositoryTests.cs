using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MailTriage.Core.Models;
using MailTriage.Infrastructure.Data;

namespace MailTriage.Tests.Data;

public class EmailRepositoryTests : IDisposable
{
    private readonly MailTriageDbContext _context;
    private readonly EmailRepository _repository;

    public EmailRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<MailTriageDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _context = new MailTriageDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();
        _repository = new EmailRepository(_context);
    }

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Fact]
    public async Task GetMailAccountsAsync_ReturnsOnlyEnabledAccounts()
    {
        _context.MailAccounts.AddRange(
            new MailAccount { Name = "Active", Host = "imap.example.com", Username = "user1@example.com", IsEnabled = true },
            new MailAccount { Name = "Disabled", Host = "imap.other.com", Username = "user2@other.com", IsEnabled = false }
        );
        await _context.SaveChangesAsync();

        var result = await _repository.GetMailAccountsAsync();

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Active");
    }

    [Fact]
    public async Task AddMailAccountAsync_PersistsAccount()
    {
        var account = new MailAccount
        {
            Name = "Test Account",
            Host = "imap.test.com",
            Port = 993,
            Username = "user@test.com",
            Password = "secret",
            UseSsl = true
        };

        var result = await _repository.AddMailAccountAsync(account);

        result.Id.Should().BeGreaterThan(0);
        var fromDb = await _context.MailAccounts.FindAsync(result.Id);
        fromDb.Should().NotBeNull();
        fromDb!.Name.Should().Be("Test Account");
    }

    [Fact]
    public async Task UpdateMailAccountAsync_UpdatesRecord()
    {
        var account = new MailAccount { Name = "Old Name", Host = "imap.test.com", Username = "user@test.com" };
        _context.MailAccounts.Add(account);
        await _context.SaveChangesAsync();

        account.Name = "New Name";
        await _repository.UpdateMailAccountAsync(account);

        var fromDb = await _context.MailAccounts.FindAsync(account.Id);
        fromDb!.Name.Should().Be("New Name");
    }

    [Fact]
    public async Task DeleteMailAccountAsync_RemovesAccount()
    {
        var account = new MailAccount { Name = "To Delete", Host = "imap.test.com", Username = "del@test.com" };
        _context.MailAccounts.Add(account);
        await _context.SaveChangesAsync();
        var id = account.Id;

        await _repository.DeleteMailAccountAsync(id);

        var fromDb = await _context.MailAccounts.FindAsync(id);
        fromDb.Should().BeNull();
    }

    [Fact]
    public async Task IsMessageAlreadyProcessedAsync_ReturnsFalseForNewMessage()
    {
        var account = new MailAccount { Name = "Acc", Host = "h.com", Username = "u@h.com" };
        _context.MailAccounts.Add(account);
        await _context.SaveChangesAsync();

        var result = await _repository.IsMessageAlreadyProcessedAsync(account.Id, "msg-new@test");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsMessageAlreadyProcessedAsync_ReturnsTrueForExistingMessage()
    {
        var account = new MailAccount { Name = "Acc", Host = "h.com", Username = "u@h.com" };
        _context.MailAccounts.Add(account);
        await _context.SaveChangesAsync();

        _context.TriagedEmails.Add(new TriagedEmail
        {
            MailAccountId = account.Id,
            MessageId = "msg-123@test",
            Subject = "Test",
            FromAddress = "from@test.com",
            ReceivedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var result = await _repository.IsMessageAlreadyProcessedAsync(account.Id, "msg-123@test");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task SaveTriagedEmailAsync_PersistsEmail()
    {
        var account = new MailAccount { Name = "Acc", Host = "h.com", Username = "u@h.com" };
        _context.MailAccounts.Add(account);
        await _context.SaveChangesAsync();

        var email = new TriagedEmail
        {
            MailAccountId = account.Id,
            MessageId = "save-test@test",
            Subject = "Hello",
            FromAddress = "sender@test.com",
            Category = TriageCategory.ActionRequired,
            Priority = TriagePriority.High,
            ReceivedAt = DateTime.UtcNow
        };

        var result = await _repository.SaveTriagedEmailAsync(email);

        result.Id.Should().BeGreaterThan(0);
        var fromDb = await _context.TriagedEmails.FindAsync(result.Id);
        fromDb!.Category.Should().Be(TriageCategory.ActionRequired);
        fromDb.Priority.Should().Be(TriagePriority.High);
    }

    [Fact]
    public async Task GetTriagedEmailsAsync_FiltersByCategory()
    {
        var account = new MailAccount { Name = "Acc", Host = "h.com", Username = "u@h.com" };
        _context.MailAccounts.Add(account);
        await _context.SaveChangesAsync();

        _context.TriagedEmails.AddRange(
            new TriagedEmail { MailAccountId = account.Id, MessageId = "m1", Category = TriageCategory.Spam, ReceivedAt = DateTime.UtcNow, FromAddress = "a@b.com" },
            new TriagedEmail { MailAccountId = account.Id, MessageId = "m2", Category = TriageCategory.ActionRequired, ReceivedAt = DateTime.UtcNow, FromAddress = "a@b.com" },
            new TriagedEmail { MailAccountId = account.Id, MessageId = "m3", Category = TriageCategory.ActionRequired, ReceivedAt = DateTime.UtcNow, FromAddress = "a@b.com" }
        );
        await _context.SaveChangesAsync();

        var result = await _repository.GetTriagedEmailsAsync(category: TriageCategory.ActionRequired);

        result.Should().HaveCount(2);
        result.Should().AllSatisfy(e => e.Category.Should().Be(TriageCategory.ActionRequired));
    }

    [Fact]
    public async Task GetTriagedEmailsAsync_FiltersByMinPriority()
    {
        var account = new MailAccount { Name = "Acc", Host = "h.com", Username = "u@h.com" };
        _context.MailAccounts.Add(account);
        await _context.SaveChangesAsync();

        _context.TriagedEmails.AddRange(
            new TriagedEmail { MailAccountId = account.Id, MessageId = "p1", Priority = TriagePriority.Low, ReceivedAt = DateTime.UtcNow, FromAddress = "a@b.com" },
            new TriagedEmail { MailAccountId = account.Id, MessageId = "p2", Priority = TriagePriority.High, ReceivedAt = DateTime.UtcNow, FromAddress = "a@b.com" },
            new TriagedEmail { MailAccountId = account.Id, MessageId = "p3", Priority = TriagePriority.Urgent, ReceivedAt = DateTime.UtcNow, FromAddress = "a@b.com" }
        );
        await _context.SaveChangesAsync();

        var result = await _repository.GetTriagedEmailsAsync(minPriority: TriagePriority.High);

        result.Should().HaveCount(2);
        result.Should().AllSatisfy(e => ((int)e.Priority).Should().BeGreaterThanOrEqualTo((int)TriagePriority.High));
    }

    [Fact]
    public async Task ForwardingRules_AddAndRetrieve()
    {
        var rule = new ForwardingRule
        {
            Name = "Urgent Forward",
            ForwardToAddress = "admin@example.com",
            MinPriority = TriagePriority.Urgent,
            IsEnabled = true
        };

        var added = await _repository.AddForwardingRuleAsync(rule);
        var rules = await _repository.GetForwardingRulesAsync();

        added.Id.Should().BeGreaterThan(0);
        rules.Should().HaveCount(1);
        rules[0].ForwardToAddress.Should().Be("admin@example.com");
    }
}
