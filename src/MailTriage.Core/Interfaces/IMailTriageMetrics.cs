namespace MailTriage.Core.Interfaces;

/// <summary>
/// Records operational metrics for the Mail Triage service.
/// Implementations must be thread-safe; a singleton is registered for the process lifetime.
/// </summary>
public interface IMailTriageMetrics
{
    /// <summary>Records the outcome and wall-clock duration of a single poll cycle across all accounts.</summary>
    void RecordPollRun(bool success, double durationSeconds);

    /// <summary>Increments the total count of emails processed (triaged) by the polling service.</summary>
    void RecordEmailProcessed();

    /// <summary>Records the outcome of a triage request (automated or manual).</summary>
    void RecordTriageRequest(bool success);

    /// <summary>Records the outcome of an email forward attempt.</summary>
    void RecordForwardAttempt(bool success);

    /// <summary>Returns an atomic point-in-time snapshot suitable for rendering.</summary>
    MetricsSnapshot GetSnapshot();
}

/// <summary>Immutable snapshot of all metric values at a point in time.</summary>
public sealed record MetricsSnapshot(
    long PollRunsSuccess,
    long PollRunsFailure,
    double PollDurationSum,
    long PollDurationCount,
    long[] PollDurationBucketCounts,
    double[] PollDurationBuckets,
    long EmailsProcessedTotal,
    long TriageRequestsSuccess,
    long TriageRequestsFailure,
    long ForwardAttemptsSuccess,
    long ForwardAttemptsFailure
);
