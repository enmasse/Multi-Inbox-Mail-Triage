using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using MailTriage.Api.Services;
using MailTriage.Core.Interfaces;

namespace MailTriage.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StatusController : ControllerBase
{
    private static readonly DateTime StartedAt = DateTime.UtcNow;

    private readonly IPollingStateStore _pollingStore;
    private readonly DependencyHealthService _healthService;

    public StatusController(IPollingStateStore pollingStore, DependencyHealthService healthService)
    {
        _pollingStore = pollingStore;
        _healthService = healthService;
    }

    /// <summary>
    /// Returns a fast, non-blocking snapshot of service health, dependency reachability,
    /// and per-account polling state.
    /// </summary>
    [HttpGet]
    public IActionResult Get()
    {
        var now = DateTime.UtcNow;
        var version = Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";

        var db = _healthService.GetDatabaseStatus();
        var ollama = _healthService.GetOllamaStatus();
        var accounts = _pollingStore.GetAllAccountStates();

        return Ok(new
        {
            startedAt = StartedAt,
            currentTime = now,
            uptimeSeconds = (long)(now - StartedAt).TotalSeconds,
            version,
            polling = new
            {
                isRunning = _pollingStore.IsRunning,
                accounts = accounts.Select(a => new
                {
                    accountId = a.AccountId,
                    isEnabled = a.IsEnabled,
                    mailbox = a.Mailbox,
                    lastPollStartedAt = a.LastPollStartedAt,
                    lastPollCompletedAt = a.LastPollCompletedAt,
                    lastPollSucceeded = a.LastPollSucceeded,
                    lastError = a.LastError,
                    lastMessageIdProcessed = a.LastMessageIdProcessed
                })
            },
            dependencies = new
            {
                database = new
                {
                    isReachable = db.IsReachable,
                    checkedAt = db.CheckedAt == DateTime.MinValue ? (DateTime?)null : db.CheckedAt
                },
                ollama = new
                {
                    isReachable = ollama.IsReachable,
                    checkedAt = ollama.CheckedAt == DateTime.MinValue ? (DateTime?)null : ollama.CheckedAt
                }
            }
        });
    }
}
