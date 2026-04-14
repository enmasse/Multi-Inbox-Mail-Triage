using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using MailTriage.Core.Interfaces;
using MailTriage.Core.Metrics;

namespace MailTriage.Api.Controllers;

/// <summary>
/// Exposes operational metrics at <c>GET /api/metrics</c> in Prometheus text format (v0.0.4).
///
/// The endpoint is intentionally unauthenticated so that monitoring scrapers can reach it
/// without credentials. It does <b>not</b> emit any PII: email addresses, message subjects,
/// or other user data are never used as label values.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class MetricsController : ControllerBase
{
    private const string PrometheusContentType = "text/plain; version=0.0.4; charset=utf-8";

    private readonly IMailTriageMetrics _metrics;

    public MetricsController(IMailTriageMetrics metrics)
    {
        _metrics = metrics;
    }

    /// <summary>Returns current metrics in Prometheus text exposition format.</summary>
    [HttpGet]
    [Produces("text/plain")]
    public IActionResult GetMetrics()
    {
        var snapshot = _metrics.GetSnapshot();
        var body = RenderPrometheus(snapshot);
        return Content(body, PrometheusContentType);
    }

    // ── Prometheus text format rendering ─────────────────────────────────────

    internal static string RenderPrometheus(MetricsSnapshot s)
    {
        var sb = new StringBuilder(512);

        // mailtriage_poll_runs_total
        sb.AppendLine("# HELP mailtriage_poll_runs_total Total number of mail poll cycles executed.");
        sb.AppendLine("# TYPE mailtriage_poll_runs_total counter");
        sb.AppendLine(Counter("mailtriage_poll_runs_total", "result", "success", s.PollRunsSuccess));
        sb.AppendLine(Counter("mailtriage_poll_runs_total", "result", "failure", s.PollRunsFailure));

        // mailtriage_poll_duration_seconds (histogram)
        sb.AppendLine("# HELP mailtriage_poll_duration_seconds Wall-clock duration of each poll cycle in seconds.");
        sb.AppendLine("# TYPE mailtriage_poll_duration_seconds histogram");
        for (int i = 0; i < s.PollDurationBuckets.Length; i++)
        {
            var le = FormatBucketLabel(s.PollDurationBuckets[i]);
            sb.AppendLine($"mailtriage_poll_duration_seconds_bucket{{le=\"{le}\"}} {s.PollDurationBucketCounts[i]}");
        }
        // +Inf bucket is stored at index PollDurationBuckets.Length
        sb.AppendLine($"mailtriage_poll_duration_seconds_bucket{{le=\"+Inf\"}} {s.PollDurationBucketCounts[s.PollDurationBuckets.Length]}");
        sb.AppendLine($"mailtriage_poll_duration_seconds_sum {FormatDouble(s.PollDurationSum)}");
        sb.AppendLine($"mailtriage_poll_duration_seconds_count {s.PollDurationCount}");

        // mailtriage_emails_processed_total
        sb.AppendLine("# HELP mailtriage_emails_processed_total Total emails triaged by the polling service.");
        sb.AppendLine("# TYPE mailtriage_emails_processed_total counter");
        sb.AppendLine($"mailtriage_emails_processed_total {s.EmailsProcessedTotal}");

        // mailtriage_triage_requests_total
        sb.AppendLine("# HELP mailtriage_triage_requests_total Total triage requests (automated polling + manual API calls).");
        sb.AppendLine("# TYPE mailtriage_triage_requests_total counter");
        sb.AppendLine(Counter("mailtriage_triage_requests_total", "result", "success", s.TriageRequestsSuccess));
        sb.AppendLine(Counter("mailtriage_triage_requests_total", "result", "failure", s.TriageRequestsFailure));

        // mailtriage_forward_attempts_total
        sb.AppendLine("# HELP mailtriage_forward_attempts_total Total email forward attempts via SMTP.");
        sb.AppendLine("# TYPE mailtriage_forward_attempts_total counter");
        sb.AppendLine(Counter("mailtriage_forward_attempts_total", "result", "success", s.ForwardAttemptsSuccess));
        sb.AppendLine(Counter("mailtriage_forward_attempts_total", "result", "failure", s.ForwardAttemptsFailure));

        return sb.ToString();
    }

    private static string Counter(string name, string labelKey, string labelValue, long value) =>
        $"{name}{{{labelKey}=\"{labelValue}\"}} {value}";

    private static string FormatDouble(double value) =>
        value.ToString("G", CultureInfo.InvariantCulture);

    /// <summary>
    /// Formats a histogram bucket upper-bound value the way Prometheus expects:
    /// integer-valued doubles are rendered without a decimal point (e.g. <c>1</c> not <c>1.0</c>).
    /// </summary>
    internal static string FormatBucketLabel(double value) =>
        value % 1 == 0
            ? ((long)value).ToString(CultureInfo.InvariantCulture)
            : value.ToString("G", CultureInfo.InvariantCulture);
}
