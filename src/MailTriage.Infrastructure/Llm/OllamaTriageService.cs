using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MailTriage.Core.Interfaces;
using MailTriage.Core.Models;

namespace MailTriage.Infrastructure.Llm;

public class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "llama3.2";
    public int TimeoutSeconds { get; set; } = 60;
}

public class OllamaTriageService : ITriageService
{
    private readonly HttpClient _httpClient;
    private readonly OllamaOptions _options;
    private readonly ILogger<OllamaTriageService> _logger;

    public OllamaTriageService(HttpClient httpClient, IOptions<OllamaOptions> options, ILogger<OllamaTriageService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<TriageResult> TriageEmailAsync(
        string subject,
        string fromAddress,
        string bodyText,
        CancellationToken cancellationToken = default)
    {
        var truncatedBody = bodyText.Length > 2000 ? bodyText[..2000] : bodyText;
        var prompt = $$"""
            You are an email triage assistant. Analyze the following email and respond with ONLY valid JSON (no markdown, no code fences).

            Email:
            From: {{fromAddress}}
            Subject: {{subject}}
            Body: {{truncatedBody}}

            Respond with this exact JSON structure:
            {
              "category": "<one of: ActionRequired, FYI, Newsletter, Spam, Meeting, Invoice, Support, Personal, Automated, Unknown>",
              "priority": "<one of: Low, Normal, High, Urgent>",
              "summary": "<one sentence summary>",
              "actionRequired": "<what action is needed, or empty string if none>",
              "labels": ["<label1>", "<label2>"]
            }
            """;

        try
        {
            var request = new OllamaRequest
            {
                Model = _options.Model,
                Prompt = prompt,
                Stream = false
            };

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            var response = await _httpClient.PostAsJsonAsync("/api/generate", request, cts.Token);
            response.EnsureSuccessStatusCode();

            var ollamaResponse = await response.Content.ReadFromJsonAsync<OllamaResponse>(cancellationToken: cts.Token);
            if (ollamaResponse?.Response == null) return FallbackResult();

            return ParseTriageResult(ollamaResponse.Response);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Ollama triage failed for email '{Subject}', using fallback", subject);
            return FallbackResult();
        }
    }

    private static TriageResult ParseTriageResult(string json)
    {
        // Strip markdown code fences if present
        var cleaned = json.Trim();
        if (cleaned.StartsWith("```")) cleaned = string.Join('\n', cleaned.Split('\n').Skip(1));
        if (cleaned.EndsWith("```")) cleaned = cleaned[..cleaned.LastIndexOf("```")];
        cleaned = cleaned.Trim();

        try
        {
            var doc = JsonDocument.Parse(cleaned);
            var root = doc.RootElement;

            Enum.TryParse<TriageCategory>(root.GetProperty("category").GetString(), true, out var category);
            Enum.TryParse<TriagePriority>(root.GetProperty("priority").GetString(), true, out var priority);

            var summary = root.TryGetProperty("summary", out var s) ? s.GetString() ?? string.Empty : string.Empty;
            var actionRequired = root.TryGetProperty("actionRequired", out var a) ? a.GetString() ?? string.Empty : string.Empty;
            var labels = root.TryGetProperty("labels", out var l)
                ? l.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToList()
                : new List<string>();

            return new TriageResult(category, priority, summary, actionRequired, labels);
        }
        catch
        {
            return FallbackResult();
        }
    }

    private static TriageResult FallbackResult() =>
        new(TriageCategory.Unknown, TriagePriority.Normal, "Unable to triage email automatically.", string.Empty, Array.Empty<string>());

    private record OllamaRequest
    {
        [JsonPropertyName("model")] public string Model { get; init; } = string.Empty;
        [JsonPropertyName("prompt")] public string Prompt { get; init; } = string.Empty;
        [JsonPropertyName("stream")] public bool Stream { get; init; }
    }

    private record OllamaResponse
    {
        [JsonPropertyName("response")] public string? Response { get; init; }
    }
}
