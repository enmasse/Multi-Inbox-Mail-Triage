using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace MailTriage.IntegrationTests.Infrastructure;

/// <summary>
/// xUnit class fixture that manages a GreenMail Docker container for integration tests.
/// GreenMail provides real IMAP and SMTP servers in a container.
/// Ports are dynamically assigned to avoid conflicts.
/// </summary>
public sealed class GreenMailContainerFixture : IAsyncLifetime
{
    private readonly IContainer _container;

    public int SmtpPort { get; private set; }
    public int ImapPort { get; private set; }
    public string Host => "localhost";

    public GreenMailContainerFixture()
    {
        _container = new ContainerBuilder("greenmail/standalone:2.1.4")
            .WithPortBinding(3025, true)
            .WithPortBinding(3143, true)
            .WithEnvironment("GREENMAIL_OPTS",
                "-Dgreenmail.setup.test.all " +
                "-Dgreenmail.hostname=0.0.0.0 " +
                "-Dgreenmail.auth.disabled=true " +
                "-Dgreenmail.users.create=user1@example.com:password")
            .WithWaitStrategy(
                Wait.ForUnixContainer()
                    .UntilInternalTcpPortIsAvailable(3025)
                    .UntilInternalTcpPortIsAvailable(3143))
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        SmtpPort = _container.GetMappedPublicPort(3025);
        ImapPort = _container.GetMappedPublicPort(3143);
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    /// <summary>
    /// Injects an email into GreenMail via SMTP so it lands in the IMAP inbox of the
    /// recipient address.
    /// </summary>
    public async Task InjectEmailAsync(
        string from, string to, string subject, string body,
        CancellationToken cancellationToken = default)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Sender", from));
        message.To.Add(new MailboxAddress("Recipient", to));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient();
        await client.ConnectAsync(Host, SmtpPort, SecureSocketOptions.None, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}
