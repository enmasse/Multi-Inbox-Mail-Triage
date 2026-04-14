using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MailTriage.Infrastructure.Data;
using MailTriage.Infrastructure.Llm;

namespace MailTriage.Api.Services;

/// <summary>Cached health state for a single dependency.</summary>
public sealed record DependencyStatus(bool IsReachable, DateTime CheckedAt);

/// <summary>
/// Performs non-blocking, time-boxed connectivity checks for the database and Ollama.
/// Results are cached for <see cref="CacheTtl"/>; stale results are returned immediately
/// while a background refresh is triggered, so callers never wait for a slow/broken
/// downstream service.
/// </summary>
public sealed class DependencyHealthService
{
    internal static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(3);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<OllamaOptions> _ollamaOptions;
    private readonly IHttpClientFactory _httpClientFactory;

    private volatile DependencyStatus _dbStatus = new(false, DateTime.MinValue);
    private volatile DependencyStatus _ollamaStatus = new(false, DateTime.MinValue);
    private int _refreshing;

    public DependencyHealthService(
        IServiceScopeFactory scopeFactory,
        IOptions<OllamaOptions> ollamaOptions,
        IHttpClientFactory httpClientFactory)
    {
        _scopeFactory = scopeFactory;
        _ollamaOptions = ollamaOptions;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Returns the current (possibly cached) health of the database.
    /// If the cached value is stale a background refresh is queued.
    /// </summary>
    public DependencyStatus GetDatabaseStatus() => GetOrTriggerRefresh(_dbStatus);

    /// <summary>
    /// Returns the current (possibly cached) health of Ollama.
    /// If the cached value is stale a background refresh is queued.
    /// </summary>
    public DependencyStatus GetOllamaStatus() => GetOrTriggerRefresh(_ollamaStatus);

    private DependencyStatus GetOrTriggerRefresh(DependencyStatus current)
    {
        if (DateTime.UtcNow - current.CheckedAt < CacheTtl)
            return current;

        // Trigger a single background refresh; ignore if one is already running.
        if (Interlocked.CompareExchange(ref _refreshing, 1, 0) == 0)
            _ = RefreshAsync().ContinueWith(_ => Interlocked.Exchange(ref _refreshing, 0));

        return current;
    }

    /// <summary>
    /// Forces an immediate refresh of both checks and waits for the result.
    /// Used on the first call so the initial status response contains real data.
    /// </summary>
    public async Task RefreshAsync()
    {
        var dbTask = CheckDatabaseAsync();
        var ollamaTask = CheckOllamaAsync();
        await Task.WhenAll(dbTask, ollamaTask).ConfigureAwait(false);
        _dbStatus = await dbTask;
        _ollamaStatus = await ollamaTask;
    }

    private async Task<DependencyStatus> CheckDatabaseAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MailTriageDbContext>();
            using var cts = new CancellationTokenSource(CheckTimeout);
            var ok = await db.Database.CanConnectAsync(cts.Token).ConfigureAwait(false);
            return new DependencyStatus(ok, DateTime.UtcNow);
        }
        catch
        {
            return new DependencyStatus(false, DateTime.UtcNow);
        }
    }

    private async Task<DependencyStatus> CheckOllamaAsync()
    {
        try
        {
            var client = _httpClientFactory.CreateClient("OllamaHealth");
            using var cts = new CancellationTokenSource(CheckTimeout);
            var response = await client.GetAsync("/api/tags", cts.Token).ConfigureAwait(false);
            return new DependencyStatus(response.IsSuccessStatusCode, DateTime.UtcNow);
        }
        catch
        {
            return new DependencyStatus(false, DateTime.UtcNow);
        }
    }
}
