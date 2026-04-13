namespace MailTriage.Core.Interfaces;

using MailTriage.Core.Models;

public interface IMailMonitorService
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TriagedEmail>> PollAccountAsync(MailAccount account, CancellationToken cancellationToken = default);
}
