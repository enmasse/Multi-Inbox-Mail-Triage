using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MailTriage.Core.Interfaces;
using MailTriage.Core.Models;

namespace MailTriage.Api.BackgroundServices;

public class MailPollingService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MailPollingService> _logger;

    public MailPollingService(IServiceScopeFactory scopeFactory, ILogger<MailPollingService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Mail polling service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            await PollAllAccountsAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);
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

            var tasks = accounts.Select(account => PollAccountSafeAsync(account.Id, cancellationToken));
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during mail polling cycle");
        }
    }

    private async Task PollAccountSafeAsync(int accountId, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IEmailRepository>();
            var account = await repository.GetMailAccountAsync(accountId, cancellationToken);
            if (account == null || !account.IsEnabled) return;

            var monitor = scope.ServiceProvider.GetRequiredService<IMailMonitorService>();
            var results = await monitor.PollAccountAsync(account, cancellationToken);
            if (results.Count > 0)
                _logger.LogInformation("Account {Name}: processed {Count} new emails", account.Name, results.Count);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error polling account ID {AccountId}", accountId);
        }
    }
}
