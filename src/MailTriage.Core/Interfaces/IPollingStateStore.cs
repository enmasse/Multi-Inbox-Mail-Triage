using MailTriage.Core.Models;

namespace MailTriage.Core.Interfaces;

/// <summary>
/// Thread-safe in-memory store for per-account polling telemetry.
/// The background polling service writes to this store; /api/status reads from it.
/// </summary>
public interface IPollingStateStore
{
    /// <summary>Whether the polling background service is currently running.</summary>
    bool IsRunning { get; }

    void SetRunning(bool isRunning);

    /// <summary>Called at the start of each account poll cycle.</summary>
    void RecordPollStart(int accountId, bool isEnabled, string mailbox);

    /// <summary>Called when a poll cycle completes successfully.</summary>
    void RecordPollSuccess(int accountId, string? lastMessageId = null);

    /// <summary>Called when a poll cycle fails.</summary>
    void RecordPollFailure(int accountId, string errorMessage);

    /// <summary>Returns a snapshot of all known account states.</summary>
    IReadOnlyList<AccountPollingState> GetAllAccountStates();

    /// <summary>Returns the state for a specific account, or null if never polled.</summary>
    AccountPollingState? GetAccountState(int accountId);
}
