namespace MailTriage.Core.Interfaces;

using MailTriage.Core.Models;

public interface ITriageService
{
    Task<TriageResult> TriageEmailAsync(string subject, string fromAddress, string bodyText, CancellationToken cancellationToken = default);
}

public record TriageResult(
    TriageCategory Category,
    TriagePriority Priority,
    string Summary,
    string ActionRequired,
    IReadOnlyList<string> Labels
);
