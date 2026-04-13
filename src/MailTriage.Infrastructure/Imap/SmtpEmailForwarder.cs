using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using MailTriage.Core.Interfaces;
using MailTriage.Core.Models;

namespace MailTriage.Infrastructure.Imap;

public class SmtpOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = false;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "Mail Triage Service";
}

public class SmtpEmailForwarder : IEmailForwarder
{
    private readonly SmtpOptions _options;
    private readonly ILogger<SmtpEmailForwarder> _logger;

    public SmtpEmailForwarder(IOptions<SmtpOptions> options, ILogger<SmtpEmailForwarder> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> ForwardEmailAsync(TriagedEmail email, string toAddress, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_options.Host))
        {
            _logger.LogWarning("SMTP not configured; skipping forward of email {Subject}", email.Subject);
            return false;
        }

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
            message.To.Add(MailboxAddress.Parse(toAddress));
            message.Subject = $"[Triaged: {email.Category}/{email.Priority}] {email.Subject}";

            var body = new TextPart("plain")
            {
                Text = $"""
                    --- Mail Triage Summary ---
                    Category: {email.Category}
                    Priority: {email.Priority}
                    From: {email.FromName} <{email.FromAddress}>
                    Received: {email.ReceivedAt:u}
                    Summary: {email.Summary}
                    Action Required: {email.ActionRequired}
                    ---
                    {email.BodyText}
                    """
            };
            message.Body = body;

            using var client = new SmtpClient();
            var secureOptions = _options.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;
            await client.ConnectAsync(_options.Host, _options.Port, secureOptions, cancellationToken);
            if (!string.IsNullOrEmpty(_options.Username))
                await client.AuthenticateAsync(_options.Username, _options.Password, cancellationToken);
            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            _logger.LogInformation("Forwarded email '{Subject}' to {Address}", email.Subject, toAddress);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to forward email '{Subject}' to {Address}", email.Subject, toAddress);
            return false;
        }
    }
}
