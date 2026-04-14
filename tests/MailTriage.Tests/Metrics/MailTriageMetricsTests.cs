using FluentAssertions;
using MailTriage.Core.Metrics;

namespace MailTriage.Tests.Metrics;

public class MailTriageMetricsTests
{
    private static MailTriageMetrics CreateMetrics() => new();

    // ── Poll run counters ─────────────────────────────────────────────────────

    [Fact]
    public void RecordPollRun_Success_IncrementsPollRunsSuccess()
    {
        var m = CreateMetrics();

        m.RecordPollRun(true, 0.5);

        var s = m.GetSnapshot();
        s.PollRunsSuccess.Should().Be(1);
        s.PollRunsFailure.Should().Be(0);
    }

    [Fact]
    public void RecordPollRun_Failure_IncrementsPollRunsFailure()
    {
        var m = CreateMetrics();

        m.RecordPollRun(false, 0.1);

        var s = m.GetSnapshot();
        s.PollRunsSuccess.Should().Be(0);
        s.PollRunsFailure.Should().Be(1);
    }

    [Fact]
    public void RecordPollRun_MultipleCallsMix_CountedCorrectly()
    {
        var m = CreateMetrics();

        m.RecordPollRun(true, 0.3);
        m.RecordPollRun(true, 0.7);
        m.RecordPollRun(false, 1.2);

        var s = m.GetSnapshot();
        s.PollRunsSuccess.Should().Be(2);
        s.PollRunsFailure.Should().Be(1);
    }

    // ── Poll duration histogram ───────────────────────────────────────────────

    [Fact]
    public void RecordPollRun_IncrementsHistogramCount()
    {
        var m = CreateMetrics();

        m.RecordPollRun(true, 0.5);
        m.RecordPollRun(true, 1.5);

        m.GetSnapshot().PollDurationCount.Should().Be(2);
    }

    [Fact]
    public void RecordPollRun_AccumulatesSum()
    {
        var m = CreateMetrics();

        m.RecordPollRun(true, 1.0);
        m.RecordPollRun(true, 2.0);

        m.GetSnapshot().PollDurationSum.Should().BeApproximately(3.0, 0.001);
    }

    [Theory]
    [InlineData(0.05, 9)]   // 0.05 ≤ 0.1, 0.25, 0.5, 1.0, 2.5, 5.0, 10.0, 30.0, 60.0 → all 9 buckets + +Inf
    [InlineData(0.3, 7)]    // 0.3 ≤ 0.5, 1.0, 2.5, 5.0, 10.0, 30.0, 60.0 → 7 buckets + +Inf
    [InlineData(100.0, 0)]  // 100 > 60 → no finite bucket
    public void RecordPollRun_BucketCountsAreCorrect(double duration, int expectedFiniteBuckets)
    {
        var m = CreateMetrics();

        m.RecordPollRun(true, duration);

        var s = m.GetSnapshot();
        var totalFiniteBuckets = MailTriageMetrics.HistogramBuckets.Length;

        // Count how many finite buckets got incremented
        int incrementedFinite = 0;
        for (int i = 0; i < totalFiniteBuckets; i++)
        {
            if (s.PollDurationBucketCounts[i] == 1) incrementedFinite++;
        }
        incrementedFinite.Should().Be(expectedFiniteBuckets);

        // +Inf always incremented
        s.PollDurationBucketCounts[totalFiniteBuckets].Should().Be(1);
    }

    // ── Email processed ───────────────────────────────────────────────────────

    [Fact]
    public void RecordEmailProcessed_IncrementsCounter()
    {
        var m = CreateMetrics();

        m.RecordEmailProcessed();
        m.RecordEmailProcessed();

        m.GetSnapshot().EmailsProcessedTotal.Should().Be(2);
    }

    // ── Triage requests ───────────────────────────────────────────────────────

    [Fact]
    public void RecordTriageRequest_Success_IncrementsSuccessCounter()
    {
        var m = CreateMetrics();

        m.RecordTriageRequest(true);

        var s = m.GetSnapshot();
        s.TriageRequestsSuccess.Should().Be(1);
        s.TriageRequestsFailure.Should().Be(0);
    }

    [Fact]
    public void RecordTriageRequest_Failure_IncrementsFailureCounter()
    {
        var m = CreateMetrics();

        m.RecordTriageRequest(false);

        var s = m.GetSnapshot();
        s.TriageRequestsSuccess.Should().Be(0);
        s.TriageRequestsFailure.Should().Be(1);
    }

    // ── Forward attempts ──────────────────────────────────────────────────────

    [Fact]
    public void RecordForwardAttempt_Success_IncrementsSuccessCounter()
    {
        var m = CreateMetrics();

        m.RecordForwardAttempt(true);

        m.GetSnapshot().ForwardAttemptsSuccess.Should().Be(1);
    }

    [Fact]
    public void RecordForwardAttempt_Failure_IncrementsFailureCounter()
    {
        var m = CreateMetrics();

        m.RecordForwardAttempt(false);

        m.GetSnapshot().ForwardAttemptsFailure.Should().Be(1);
    }

    // ── Snapshot consistency ──────────────────────────────────────────────────

    [Fact]
    public void GetSnapshot_FreshMetrics_AllCountersAreZero()
    {
        var m = CreateMetrics();

        var s = m.GetSnapshot();

        s.PollRunsSuccess.Should().Be(0);
        s.PollRunsFailure.Should().Be(0);
        s.PollDurationCount.Should().Be(0);
        s.PollDurationSum.Should().Be(0);
        s.EmailsProcessedTotal.Should().Be(0);
        s.TriageRequestsSuccess.Should().Be(0);
        s.TriageRequestsFailure.Should().Be(0);
        s.ForwardAttemptsSuccess.Should().Be(0);
        s.ForwardAttemptsFailure.Should().Be(0);
        s.PollDurationBucketCounts.Should().AllBeEquivalentTo(0L);
    }

    [Fact]
    public void GetSnapshot_BucketCountsLength_MatchesBucketsLengthPlusOne()
    {
        var m = CreateMetrics();

        var s = m.GetSnapshot();

        // Should have one slot per bucket boundary plus one slot for +Inf
        s.PollDurationBucketCounts.Length.Should().Be(MailTriageMetrics.HistogramBuckets.Length + 1);
    }

    // ── Concurrency smoke-test ────────────────────────────────────────────────

    [Fact]
    public async Task ConcurrentRecordCalls_ProduceConsistentTotals()
    {
        var m = CreateMetrics();
        const int iterations = 500;

        var tasks = Enumerable.Range(0, iterations).Select(i => Task.Run(() =>
        {
            m.RecordPollRun(i % 3 != 0, 0.1);
            m.RecordEmailProcessed();
            m.RecordTriageRequest(i % 5 != 0);
            m.RecordForwardAttempt(i % 7 != 0);
        }));

        await Task.WhenAll(tasks);

        var s = m.GetSnapshot();
        (s.PollRunsSuccess + s.PollRunsFailure).Should().Be(iterations);
        s.EmailsProcessedTotal.Should().Be(iterations);
        (s.TriageRequestsSuccess + s.TriageRequestsFailure).Should().Be(iterations);
        (s.ForwardAttemptsSuccess + s.ForwardAttemptsFailure).Should().Be(iterations);
    }
}
