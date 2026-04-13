using Microsoft.EntityFrameworkCore;
using MailTriage.Core.Interfaces;
using MailTriage.Core.Models;

namespace MailTriage.Infrastructure.Data;

public class EmailRepository : IEmailRepository
{
    private readonly MailTriageDbContext _context;

    public EmailRepository(MailTriageDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<MailAccount>> GetMailAccountsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.MailAccounts.Where(a => a.IsEnabled).ToListAsync(cancellationToken);
    }

    public async Task<MailAccount?> GetMailAccountAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.MailAccounts.FindAsync(new object[] { id }, cancellationToken);
    }

    public async Task<MailAccount> AddMailAccountAsync(MailAccount account, CancellationToken cancellationToken = default)
    {
        _context.MailAccounts.Add(account);
        await _context.SaveChangesAsync(cancellationToken);
        return account;
    }

    public async Task<MailAccount> UpdateMailAccountAsync(MailAccount account, CancellationToken cancellationToken = default)
    {
        _context.MailAccounts.Update(account);
        await _context.SaveChangesAsync(cancellationToken);
        return account;
    }

    public async Task DeleteMailAccountAsync(int id, CancellationToken cancellationToken = default)
    {
        var account = await _context.MailAccounts.FindAsync(new object[] { id }, cancellationToken);
        if (account != null)
        {
            _context.MailAccounts.Remove(account);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<bool> IsMessageAlreadyProcessedAsync(int accountId, string messageId, CancellationToken cancellationToken = default)
    {
        return await _context.TriagedEmails.AnyAsync(e => e.MailAccountId == accountId && e.MessageId == messageId, cancellationToken);
    }

    public async Task<TriagedEmail> SaveTriagedEmailAsync(TriagedEmail email, CancellationToken cancellationToken = default)
    {
        _context.TriagedEmails.Add(email);
        await _context.SaveChangesAsync(cancellationToken);
        return email;
    }

    public async Task<IReadOnlyList<TriagedEmail>> GetTriagedEmailsAsync(
        int? accountId = null,
        TriageCategory? category = null,
        TriagePriority? minPriority = null,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        var query = _context.TriagedEmails.AsQueryable();
        if (accountId.HasValue) query = query.Where(e => e.MailAccountId == accountId.Value);
        if (category.HasValue) query = query.Where(e => e.Category == category.Value);
        if (minPriority.HasValue) query = query.Where(e => e.Priority >= minPriority.Value);
        return await query.OrderByDescending(e => e.TriagedAt).Skip(skip).Take(take).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ForwardingRule>> GetForwardingRulesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.ForwardingRules.Where(r => r.IsEnabled).ToListAsync(cancellationToken);
    }

    public async Task<ForwardingRule> AddForwardingRuleAsync(ForwardingRule rule, CancellationToken cancellationToken = default)
    {
        _context.ForwardingRules.Add(rule);
        await _context.SaveChangesAsync(cancellationToken);
        return rule;
    }

    public async Task DeleteForwardingRuleAsync(int id, CancellationToken cancellationToken = default)
    {
        var rule = await _context.ForwardingRules.FindAsync(new object[] { id }, cancellationToken);
        if (rule != null)
        {
            _context.ForwardingRules.Remove(rule);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
