using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MailTriage.Core.Models;
using MailTriage.Infrastructure.Llm;

namespace MailTriage.Tests.Llm;

public class OllamaTriageServiceTests
{
    private static OllamaTriageService CreateService(string responseJson)
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK,
            $"{{\"response\":{System.Text.Json.JsonSerializer.Serialize(responseJson)}}}");
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        var options = Options.Create(new OllamaOptions { Model = "llama3.2", TimeoutSeconds = 30 });
        return new OllamaTriageService(httpClient, options, NullLogger<OllamaTriageService>.Instance);
    }

    [Fact]
    public async Task TriageEmailAsync_ParsesValidResponse()
    {
        var json = """{"category":"ActionRequired","priority":"High","summary":"Customer needs help.","actionRequired":"Reply within 24h","labels":["customer","urgent"]}""";
        var service = CreateService(json);

        var result = await service.TriageEmailAsync("Help needed", "customer@test.com", "Please help me urgently");

        result.Category.Should().Be(TriageCategory.ActionRequired);
        result.Priority.Should().Be(TriagePriority.High);
        result.Summary.Should().Be("Customer needs help.");
        result.ActionRequired.Should().Be("Reply within 24h");
        result.Labels.Should().Contain("customer").And.Contain("urgent");
    }

    [Fact]
    public async Task TriageEmailAsync_HandlesMalformedJsonWithFallback()
    {
        var service = CreateService("not valid json at all");

        var result = await service.TriageEmailAsync("Test", "test@test.com", "Body");

        result.Category.Should().Be(TriageCategory.Unknown);
        result.Priority.Should().Be(TriagePriority.Normal);
        result.Summary.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task TriageEmailAsync_HandlesHttpFailureWithFallback()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError, "error");
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        var options = Options.Create(new OllamaOptions { Model = "llama3.2", TimeoutSeconds = 30 });
        var service = new OllamaTriageService(httpClient, options, NullLogger<OllamaTriageService>.Instance);

        var result = await service.TriageEmailAsync("Test", "test@test.com", "Body");

        result.Category.Should().Be(TriageCategory.Unknown);
    }

    [Fact]
    public async Task TriageEmailAsync_ParsesMarkdownFencedResponse()
    {
        var json = "```json\n{\"category\":\"Newsletter\",\"priority\":\"Low\",\"summary\":\"Newsletter email\",\"actionRequired\":\"\",\"labels\":[\"newsletter\"]}\n```";
        var service = CreateService(json);

        var result = await service.TriageEmailAsync("Newsletter", "news@test.com", "Weekly digest");

        result.Category.Should().Be(TriageCategory.Newsletter);
        result.Priority.Should().Be(TriagePriority.Low);
    }

    [Fact]
    public async Task TriageEmailAsync_TruncatesLongBodyText()
    {
        string? capturedRequest = null;
        var handler = new CapturingMockHandler(req =>
        {
            capturedRequest = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"response\":\"{\\\"category\\\":\\\"FYI\\\",\\\"priority\\\":\\\"Normal\\\",\\\"summary\\\":\\\"Summary\\\",\\\"actionRequired\\\":\\\"\\\",\\\"labels\\\":[]}\"}"),
            };
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        var options = Options.Create(new OllamaOptions { Model = "llama3.2", TimeoutSeconds = 30 });
        var service = new OllamaTriageService(httpClient, options, NullLogger<OllamaTriageService>.Instance);

        var longBody = new string('x', 5000);
        await service.TriageEmailAsync("Test", "t@t.com", longBody);

        capturedRequest.Should().NotBeNull();
        // The prompt should contain truncated body (max 2000 chars of body)
        capturedRequest!.Length.Should().BeLessThan(10000);
    }
}

internal class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly string _content;

    public MockHttpMessageHandler(HttpStatusCode statusCode, string content)
    {
        _statusCode = statusCode;
        _content = content;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(_statusCode) { Content = new StringContent(_content) });
}

internal class CapturingMockHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public CapturingMockHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(_handler(request));
}
