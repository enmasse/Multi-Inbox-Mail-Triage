using MailTriage.Core.Interfaces;

namespace MailTriage.Core.Metrics;

/// <summary>
/// Thread-safe, in-process implementation of <see cref="IMailTriageMetrics"/>.
/// Uses <see cref="Interlocked"/> operations for counters and a lock for the histogram sum
/// so that all reads remain consistent without blocking on the hot path.
/// </summary>
public sealed class MailTriageMetrics : IMailTriageMetrics
{
    /// <summary>Upper boundaries of the poll-duration histogram buckets (seconds).</summary>
    public static readonly double[] HistogramBuckets = { 0.1, 0.25, 0.5, 1.0, 2.5, 5.0, 10.0, 30.0, 60.0 };

    // --- Counters (all modified exclusively via Interlocked) ---
    private long _pollRunsSuccess;
    private long _pollRunsFailure;
    private long _emailsProcessedTotal;
    private long _triageRequestsSuccess;
    private long _triageRequestsFailure;
    private long _forwardAttemptsSuccess;
    private long _forwardAttemptsFailure;

    // --- Histogram ---
    // One bucket per boundary + 1 for the mandatory +Inf bucket.
    private readonly long[] _pollDurationBucketCounts = new long[HistogramBuckets.Length + 1];
    private long _pollDurationCount;
    private double _pollDurationSum;
    private readonly object _sumLock = new();

    public void RecordPollRun(bool success, double durationSeconds)
    {
        if (success)
            Interlocked.Increment(ref _pollRunsSuccess);
        else
            Interlocked.Increment(ref _pollRunsFailure);

        // Increment all buckets whose upper bound is >= the observed value (cumulative).
        for (int i = 0; i < HistogramBuckets.Length; i++)
        {
            if (durationSeconds <= HistogramBuckets[i])
                Interlocked.Increment(ref _pollDurationBucketCounts[i]);
        }
        // The +Inf bucket always increments.
        Interlocked.Increment(ref _pollDurationBucketCounts[HistogramBuckets.Length]);
        Interlocked.Increment(ref _pollDurationCount);

        // Sum is not atomic for double, but precision loss is acceptable for an ops metric.
        lock (_sumLock)
        {
            _pollDurationSum += durationSeconds;
        }
    }

    public void RecordEmailProcessed() =>
        Interlocked.Increment(ref _emailsProcessedTotal);

    public void RecordTriageRequest(bool success)
    {
        if (success)
            Interlocked.Increment(ref _triageRequestsSuccess);
        else
            Interlocked.Increment(ref _triageRequestsFailure);
    }

    public void RecordForwardAttempt(bool success)
    {
        if (success)
            Interlocked.Increment(ref _forwardAttemptsSuccess);
        else
            Interlocked.Increment(ref _forwardAttemptsFailure);
    }

    public MetricsSnapshot GetSnapshot()
    {
        double sum;
        lock (_sumLock)
        {
            sum = _pollDurationSum;
        }

        // Copy bucket counts atomically.
        var bucketCounts = new long[_pollDurationBucketCounts.Length];
        for (int i = 0; i < _pollDurationBucketCounts.Length; i++)
            bucketCounts[i] = Interlocked.Read(ref _pollDurationBucketCounts[i]);

        return new MetricsSnapshot(
            PollRunsSuccess: Interlocked.Read(ref _pollRunsSuccess),
            PollRunsFailure: Interlocked.Read(ref _pollRunsFailure),
            PollDurationSum: sum,
            PollDurationCount: Interlocked.Read(ref _pollDurationCount),
            PollDurationBucketCounts: bucketCounts,
            PollDurationBuckets: HistogramBuckets,
            EmailsProcessedTotal: Interlocked.Read(ref _emailsProcessedTotal),
            TriageRequestsSuccess: Interlocked.Read(ref _triageRequestsSuccess),
            TriageRequestsFailure: Interlocked.Read(ref _triageRequestsFailure),
            ForwardAttemptsSuccess: Interlocked.Read(ref _forwardAttemptsSuccess),
            ForwardAttemptsFailure: Interlocked.Read(ref _forwardAttemptsFailure)
        );
    }
}
