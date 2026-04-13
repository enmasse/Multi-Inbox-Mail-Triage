using MailKit.Net.Imap;
using MailKit.Security;
using MailTriage.Core.Interfaces;

namespace MailTriage.Infrastructure.Imap;

public class ImapClientFactory : IImapClientFactory
{
    public async Task<IImapClient> CreateAndConnectAsync(
        string host, int port, bool useSsl, string username, string password,
        CancellationToken cancellationToken = default)
    {
        var client = new ImapClient();
        var secureOptions = useSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;
        await client.ConnectAsync(host, port, secureOptions, cancellationToken);
        await client.AuthenticateAsync(username, password, cancellationToken);
        return client;
    }
}
