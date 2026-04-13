namespace MailTriage.Core.Interfaces;

using MailKit.Net.Imap;

public interface IImapClientFactory
{
    Task<IImapClient> CreateAndConnectAsync(string host, int port, bool useSsl, string username, string password, CancellationToken cancellationToken = default);
}
