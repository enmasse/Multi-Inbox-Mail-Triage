namespace MailTriage.Core.Interfaces;

using MailTriage.Core.Models;

public interface IEmailForwarder
{
    Task<bool> ForwardEmailAsync(TriagedEmail email, string toAddress, CancellationToken cancellationToken = default);
}
