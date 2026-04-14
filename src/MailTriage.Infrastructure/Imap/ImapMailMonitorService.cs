using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using Microsoft.Extensions.Logging;
using MailTriage.Core.Interfaces;
using MailTriage.Core.Models;
using MimeKit;
using System.Text.Json;

namespace MailTriage.Infrastructure.Imap;

public class ImapMailMonitorService : IMailMonitorService
{
    private readonly IImapClientFactory _clientFactory;
    private readonly IEmailRepository _repository;
    private readonly ITriageService _triageService;
    private readonly IEmailForwarder _forwarder;
    private readonly ILogger<ImapMailMonitorService> _logger;
    private readonly IMailTriageMetrics _metrics;

    public ImapMailMonitorService(
        IImapClientFactory clientFactory,
        IEmailRepository repository,
        ITriageService triageService,
        IEmailForwarder forwarder,
        ILogger<ImapMailMonitorService> logger,
        IMailTriageMetrics metrics)
    {
        _clientFactory = clientFactory;
        _repository = repository;
        _triageService = triageService;
        _forwarder = forwarder;
        _logger = logger;
        _metrics = metrics;
    }

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public async Task<IReadOnlyList<TriagedEmail>> PollAccountAsync(
        MailAccount account,
        CancellationToken cancellationToken = default)
    {
        var results = new List<TriagedEmail>();
        IImapClient? client = null;
        try
        {
            _logger.LogInformation("Polling account {AccountName} ({Username}@{Host})", account.Name, account.Username, account.Host);
            client = await _clientFactory.CreateAndConnectAsync(
                account.Host, account.Port, account.UseSsl,
                account.Username, account.Password, cancellationToken);

            var inbox = client.GetFolder(account.Mailbox);
            await inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

            var uids = await inbox.SearchAsync(SearchQuery.NotSeen, cancellationToken);
            _logger.LogInformation("Found {Count} unseen messages in {Account}", uids.Count, account.Name);

            foreach (var uid in uids)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var message = await inbox.GetMessageAsync(uid, cancellationToken);
                    var messageId = message.MessageId ?? uid.ToString();

                    if (await _repository.IsMessageAlreadyProcessedAsync(account.Id, messageId, cancellationToken))
                    {
                        _logger.LogDebug("Skipping already-processed message {MessageId}", messageId);
                        continue;
                    }

                    var bodyText = message.TextBody ?? message.HtmlBody ?? string.Empty;

                    TriageResult triageResult;
                    try
                    {
                        triageResult = await _triageService.TriageEmailAsync(
                            message.Subject ?? string.Empty,
                            message.From.ToString(),
                            bodyText,
                            cancellationToken);
                        _metrics.RecordTriageRequest(true);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _metrics.RecordTriageRequest(false);
                        throw;
                    }

                    var toAddresses = message.To.Select(a => a.ToString()).ToList();
                    var triaged = new TriagedEmail
                    {
                        MailAccountId = account.Id,
                        MessageId = messageId,
                        Subject = message.Subject ?? string.Empty,
                        FromAddress = message.From.Mailboxes.FirstOrDefault()?.Address ?? message.From.ToString(),
                        FromName = message.From.Mailboxes.FirstOrDefault()?.Name ?? string.Empty,
                        ToAddresses = JsonSerializer.Serialize(toAddresses),
                        BodyText = bodyText.Length > 10000 ? bodyText[..10000] : bodyText,
                        BodyHtml = (message.HtmlBody ?? string.Empty).Length > 50000 ? (message.HtmlBody ?? string.Empty)[..50000] : (message.HtmlBody ?? string.Empty),
                        ReceivedAt = message.Date.UtcDateTime,
                        TriagedAt = DateTime.UtcNow,
                        Category = triageResult.Category,
                        Priority = triageResult.Priority,
                        Summary = triageResult.Summary,
                        ActionRequired = triageResult.ActionRequired,
                        Labels = JsonSerializer.Serialize(triageResult.Labels),
                        RawHeaders = message.Headers.ToString() ?? string.Empty
                    };

                    await _repository.SaveTriagedEmailAsync(triaged, cancellationToken);
                    _metrics.RecordEmailProcessed();

                    // Apply forwarding rules
                    var rules = await _repository.GetForwardingRulesAsync(cancellationToken);
                    foreach (var rule in rules)
                    {
                        if (MatchesRule(triaged, rule))
                        {
                            _logger.LogInformation("Forwarding email {Subject} to {Address} per rule {Rule}", triaged.Subject, rule.ForwardToAddress, rule.Name);
                            var forwarded = await _forwarder.ForwardEmailAsync(triaged, rule.ForwardToAddress, cancellationToken);
                            _metrics.RecordForwardAttempt(forwarded);
                            if (forwarded && !triaged.IsForwarded)
                            {
                                triaged.IsForwarded = true;
                                triaged.ForwardedTo = rule.ForwardToAddress;
                            }
                        }
                    }

                    results.Add(triaged);
                    _logger.LogInformation("Triaged email: [{Category}/{Priority}] {Subject} from {From}", triaged.Category, triaged.Priority, triaged.Subject, triaged.FromAddress);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Error processing message UID {Uid}", uid);
                }
            }

            await inbox.CloseAsync(false, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error polling account {AccountName}", account.Name);
        }
        finally
        {
            if (client != null)
            {
                await client.DisconnectAsync(true, CancellationToken.None);
                client.Dispose();
            }
        }
        return results;
    }

    private static bool MatchesRule(TriagedEmail email, ForwardingRule rule)
    {
        if (rule.MatchCategory.HasValue && email.Category != rule.MatchCategory.Value) return false;
        if (rule.MinPriority.HasValue && email.Priority < rule.MinPriority.Value) return false;
        if (!string.IsNullOrEmpty(rule.MatchFromPattern) && !email.FromAddress.Contains(rule.MatchFromPattern, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrEmpty(rule.MatchSubjectPattern) && !email.Subject.Contains(rule.MatchSubjectPattern, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }
}
