namespace MailTriage.Core.Interfaces;

using MailTriage.Core.Models;

public interface IEmailRepository
{
    Task<IReadOnlyList<MailAccount>> GetMailAccountsAsync(CancellationToken cancellationToken = default);
    Task<MailAccount?> GetMailAccountAsync(int id, CancellationToken cancellationToken = default);
    Task<MailAccount> AddMailAccountAsync(MailAccount account, CancellationToken cancellationToken = default);
    Task<MailAccount> UpdateMailAccountAsync(MailAccount account, CancellationToken cancellationToken = default);
    Task DeleteMailAccountAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> IsMessageAlreadyProcessedAsync(int accountId, string messageId, CancellationToken cancellationToken = default);
    Task<TriagedEmail> SaveTriagedEmailAsync(TriagedEmail email, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TriagedEmail>> GetTriagedEmailsAsync(int? accountId = null, TriageCategory? category = null, TriagePriority? minPriority = null, int skip = 0, int take = 50, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ForwardingRule>> GetForwardingRulesAsync(CancellationToken cancellationToken = default);
    Task<ForwardingRule> AddForwardingRuleAsync(ForwardingRule rule, CancellationToken cancellationToken = default);
    Task DeleteForwardingRuleAsync(int id, CancellationToken cancellationToken = default);
}
