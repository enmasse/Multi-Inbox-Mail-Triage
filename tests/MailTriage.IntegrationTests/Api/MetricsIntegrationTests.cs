using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using MailTriage.IntegrationTests.Api;

namespace MailTriage.IntegrationTests.Api;

/// <summary>
/// Integration tests for <c>GET /api/metrics</c>.
///
/// Strategy:
/// 1. Verify the endpoint is reachable without authentication and returns HTTP 200.
/// 2. Verify the Prometheus text-format response contains the required metric names.
/// 3. Drive activity through other endpoints and assert that the relevant counters increment.
/// </summary>
public class MetricsIntegrationTests : IDisposable
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public MetricsIntegrationTests()
    {
        _factory = new ApiWebApplicationFactory();
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    // ── Helper ─────────────────────────────────────────────────────────────────

    private void SetupOllamaMock(string category = "FYI", string priority = "Normal")
    {
        var responsePayload = JsonSerializer.Serialize(new
        {
            response = JsonSerializer.Serialize(new
            {
                category,
                priority,
                summary = "Test summary from mock LLM",
                actionRequired = string.Empty,
                labels = Array.Empty<string>()
            })
        });

        _factory.WireMock
            .Given(Request.Create().WithPath("/api/generate").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody(responsePayload)
                .WithHeader("Content-Type", "application/json"));
    }

    private async Task<string> GetMetricsBodyAsync()
    {
        var response = await _client.GetAsync("/api/metrics");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    // ── Availability ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMetrics_ReturnsHttp200()
    {
        var response = await _client.GetAsync("/api/metrics");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetMetrics_ContentTypeIsPrometheusTextFormat()
    {
        var response = await _client.GetAsync("/api/metrics");

        response.Content.Headers.ContentType!.MediaType.Should().Be("text/plain");
    }

    // ── Required metric names ─────────────────────────────────────────────────

    [Theory]
    [InlineData("mailtriage_poll_runs_total")]
    [InlineData("mailtriage_poll_duration_seconds")]
    [InlineData("mailtriage_emails_processed_total")]
    [InlineData("mailtriage_triage_requests_total")]
    [InlineData("mailtriage_forward_attempts_total")]
    public async Task GetMetrics_ContainsRequiredMetricName(string metricName)
    {
        var body = await GetMetricsBodyAsync();

        body.Should().Contain(metricName,
            because: $"{metricName} is a required metric per the specification");
    }

    [Theory]
    [InlineData("result=\"success\"")]
    [InlineData("result=\"failure\"")]
    public async Task GetMetrics_PollRunsTotal_HasResultLabels(string label)
    {
        var body = await GetMetricsBodyAsync();

        body.Should().Contain($"mailtriage_poll_runs_total{{{label}}}");
    }

    [Theory]
    [InlineData("result=\"success\"")]
    [InlineData("result=\"failure\"")]
    public async Task GetMetrics_TriageRequestsTotal_HasResultLabels(string label)
    {
        var body = await GetMetricsBodyAsync();

        body.Should().Contain($"mailtriage_triage_requests_total{{{label}}}");
    }

    [Theory]
    [InlineData("result=\"success\"")]
    [InlineData("result=\"failure\"")]
    public async Task GetMetrics_ForwardAttemptsTotal_HasResultLabels(string label)
    {
        var body = await GetMetricsBodyAsync();

        body.Should().Contain($"mailtriage_forward_attempts_total{{{label}}}");
    }

    [Fact]
    public async Task GetMetrics_HistogramHasBucketSumAndCount()
    {
        var body = await GetMetricsBodyAsync();

        body.Should().Contain("mailtriage_poll_duration_seconds_bucket{le=");
        body.Should().Contain("mailtriage_poll_duration_seconds_sum");
        body.Should().Contain("mailtriage_poll_duration_seconds_count");
        body.Should().Contain("le=\"+Inf\"");
    }

    [Fact]
    public async Task GetMetrics_HelpAndTypeAnnotationsPresent()
    {
        var body = await GetMetricsBodyAsync();

        body.Should().Contain("# HELP mailtriage_poll_runs_total");
        body.Should().Contain("# TYPE mailtriage_poll_runs_total counter");
        body.Should().Contain("# HELP mailtriage_poll_duration_seconds");
        body.Should().Contain("# TYPE mailtriage_poll_duration_seconds histogram");
    }

    // ── Counter increments after activity ─────────────────────────────────────

    [Fact]
    public async Task GetMetrics_AfterSuccessfulManualTriage_TriageSuccessCounterIncrements()
    {
        SetupOllamaMock("Invoice", "High");

        // Baseline
        var before = await GetMetricsBodyAsync();
        var beforeSuccess = ParseCounter(before, "mailtriage_triage_requests_total", "success");

        // Trigger activity
        var triageResponse = await _client.PostAsJsonAsync("/api/triage", new
        {
            subject = "Invoice overdue",
            fromAddress = "billing@vendor.com",
            bodyText = "Pay now."
        });
        triageResponse.EnsureSuccessStatusCode();

        // Assert counter incremented
        var after = await GetMetricsBodyAsync();
        var afterSuccess = ParseCounter(after, "mailtriage_triage_requests_total", "success");

        afterSuccess.Should().Be(beforeSuccess + 1,
            because: "a successful triage call must increment the success counter");
    }

    [Fact]
    public async Task GetMetrics_AfterOllamaFallback_TriageSuccessCounterStillIncrements()
    {
        // Configure WireMock to return an error — OllamaTriageService handles this gracefully
        // by returning a fallback Unknown/Normal result rather than throwing, so the controller
        // still records a success.
        _factory.WireMock
            .Given(Request.Create().WithPath("/api/generate").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(500).WithBody("internal error"));

        // Baseline
        var before = await GetMetricsBodyAsync();
        var beforeSuccess = ParseCounter(before, "mailtriage_triage_requests_total", "success");

        // Trigger triage (Ollama will fail but the service degrades gracefully)
        await _client.PostAsJsonAsync("/api/triage", new
        {
            subject = "Test",
            fromAddress = "x@example.com",
            bodyText = "body"
        });

        // Success counter must still increment because no exception escaped the service
        var after = await GetMetricsBodyAsync();
        var afterSuccess = ParseCounter(after, "mailtriage_triage_requests_total", "success");

        afterSuccess.Should().Be(beforeSuccess + 1,
            because: "OllamaTriageService falls back gracefully on HTTP error, so the controller records success");
    }

    [Fact]
    public async Task GetMetrics_MultipleManualTriages_CounterMatchesCallCount()
    {
        SetupOllamaMock();

        const int callCount = 3;
        var before = await GetMetricsBodyAsync();
        var beforeSuccess = ParseCounter(before, "mailtriage_triage_requests_total", "success");

        for (int i = 0; i < callCount; i++)
        {
            await _client.PostAsJsonAsync("/api/triage", new
            {
                subject = $"Email {i}",
                fromAddress = "sender@example.com",
                bodyText = "Content"
            });
        }

        var after = await GetMetricsBodyAsync();
        var afterSuccess = ParseCounter(after, "mailtriage_triage_requests_total", "success");

        afterSuccess.Should().Be(beforeSuccess + callCount);
    }

    // ── PII guard ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMetrics_DoesNotContainEmailAddresses()
    {
        SetupOllamaMock();
        await _client.PostAsJsonAsync("/api/triage", new
        {
            subject = "Secret invoice",
            fromAddress = "pii-test@private-domain.example",
            bodyText = "Do not expose this address"
        });

        var body = await GetMetricsBodyAsync();

        body.Should().NotContain("pii-test@private-domain.example",
            because: "email addresses must not appear as label values to avoid PII exposure");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the numeric value of a labelled counter line from a Prometheus text payload,
    /// e.g. <c>mailtriage_triage_requests_total{result="success"} 3</c> → 3.
    /// Returns 0 when the line is not found (metric not yet recorded).
    /// </summary>
    private static long ParseCounter(string prometheusText, string metricName, string resultLabel)
    {
        var prefix = $"{metricName}{{result=\"{resultLabel}\"}}";
        foreach (var line in prometheusText.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith(prefix, StringComparison.Ordinal))
            {
                var valuePart = trimmed[prefix.Length..].Trim();
                if (long.TryParse(valuePart, out var value))
                    return value;
            }
        }
        return 0;
    }
}
