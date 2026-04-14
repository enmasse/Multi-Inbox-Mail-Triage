using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MailTriage.Api.Services;
using MailTriage.Core.Interfaces;
using MailTriage.Core.Models;

namespace MailTriage.Api.BackgroundServices;

public class MailPollingService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MailPollingService> _logger;
    private readonly IPollingStateStore _pollingStateStore;

    public MailPollingService(
        IServiceScopeFactory scopeFactory,
        ILogger<MailPollingService> logger,
        IPollingStateStore pollingStateStore)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _pollingStateStore = pollingStateStore;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Mail polling service started");
        _pollingStateStore.SetRunning(true);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await PollAllAccountsAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _pollingStateStore.SetRunning(false);
            _logger.LogInformation("Mail polling service stopped");
        }
    }

    private async Task PollAllAccountsAsync(CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<MailAccount> accounts;
            using (var scope = _scopeFactory.CreateScope())
            {
                var repository = scope.ServiceProvider.GetRequiredService<IEmailRepository>();
                accounts = await repository.GetMailAccountsAsync(cancellationToken);
            }

            _logger.LogDebug("Polling {Count} accounts", accounts.Count);

            var tasks = accounts.Select(account => PollAccountSafeAsync(account, cancellationToken));
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during mail polling cycle");
        }
    }

    private async Task PollAccountSafeAsync(MailAccount account, CancellationToken cancellationToken)
    {
        _pollingStateStore.RecordPollStart(account.Id, account.IsEnabled, account.Mailbox);
        try
        {
            if (!account.IsEnabled)
            {
                _pollingStateStore.RecordPollSuccess(account.Id);
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var monitor = scope.ServiceProvider.GetRequiredService<IMailMonitorService>();
            var results = await monitor.PollAccountAsync(account, cancellationToken);

            var lastMessageId = results.Count > 0 ? results[^1].MessageId : null;
            _pollingStateStore.RecordPollSuccess(account.Id, lastMessageId);

            if (results.Count > 0)
                _logger.LogInformation("Account {Name}: processed {Count} new emails", account.Name, results.Count);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error polling account ID {AccountId}", account.Id);
            _pollingStateStore.RecordPollFailure(account.Id, ex.Message);
        }
    }
}
