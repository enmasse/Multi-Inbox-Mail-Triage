using MailTriage.Core.Interfaces;
using MailTriage.Core.Models;

namespace MailTriage.Api.Services;

/// <summary>
/// Thread-safe, in-memory store for per-account polling state.
/// Registered as a singleton; written by <see cref="BackgroundServices.MailPollingService"/>,
/// read by the /api/status endpoint.
/// </summary>
public sealed class PollingStateStore : IPollingStateStore
{
    private readonly object _lock = new();
    private bool _isRunning;
    private readonly Dictionary<int, AccountPollingState> _states = new();

    public bool IsRunning
    {
        get { lock (_lock) return _isRunning; }
    }

    public void SetRunning(bool isRunning)
    {
        lock (_lock) _isRunning = isRunning;
    }

    public void RecordPollStart(int accountId, bool isEnabled, string mailbox)
    {
        lock (_lock)
        {
            if (!_states.TryGetValue(accountId, out var state))
            {
                state = new AccountPollingState { AccountId = accountId };
                _states[accountId] = state;
            }
            state.IsEnabled = isEnabled;
            state.Mailbox = mailbox;
            state.LastPollStartedAt = DateTime.UtcNow;
        }
    }

    public void RecordPollSuccess(int accountId, string? lastMessageId = null)
    {
        lock (_lock)
        {
            if (!_states.TryGetValue(accountId, out var state))
            {
                state = new AccountPollingState { AccountId = accountId };
                _states[accountId] = state;
            }
            state.LastPollCompletedAt = DateTime.UtcNow;
            state.LastPollSucceeded = true;
            state.LastError = null;
            if (lastMessageId != null)
                state.LastMessageIdProcessed = lastMessageId;
        }
    }

    public void RecordPollFailure(int accountId, string errorMessage)
    {
        lock (_lock)
        {
            if (!_states.TryGetValue(accountId, out var state))
            {
                state = new AccountPollingState { AccountId = accountId };
                _states[accountId] = state;
            }
            state.LastPollCompletedAt = DateTime.UtcNow;
            state.LastPollSucceeded = false;
            state.LastError = errorMessage;
        }
    }

    public IReadOnlyList<AccountPollingState> GetAllAccountStates()
    {
        lock (_lock)
            return _states.Values.Select(s => Clone(s)).ToList().AsReadOnly();
    }

    public AccountPollingState? GetAccountState(int accountId)
    {
        lock (_lock)
            return _states.TryGetValue(accountId, out var state) ? Clone(state) : null;
    }

    private static AccountPollingState Clone(AccountPollingState s) => new()
    {
        AccountId = s.AccountId,
        IsEnabled = s.IsEnabled,
        Mailbox = s.Mailbox,
        LastPollStartedAt = s.LastPollStartedAt,
        LastPollCompletedAt = s.LastPollCompletedAt,
        LastPollSucceeded = s.LastPollSucceeded,
        LastError = s.LastError,
        LastMessageIdProcessed = s.LastMessageIdProcessed
    };
}
