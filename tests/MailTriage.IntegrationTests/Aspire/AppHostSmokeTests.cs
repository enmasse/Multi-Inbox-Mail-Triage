using Aspire.Hosting.Testing;
using FluentAssertions;
using System.Net;

namespace MailTriage.IntegrationTests.Aspire;

/// <summary>
/// Smoke tests for the Aspire AppHost.
/// Verifies the application can start and the API responds to health checks
/// using the full Aspire orchestration stack.
/// </summary>
public class AppHostSmokeTests
{
    [Fact]
    public async Task AppHost_StartsSuccessfully_AndApiRespondsToHealthCheck()
    {
        // Build the distributed app using Aspire testing builder
        var builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.MailTriage_AppHost>();

        await using var app = await builder.BuildAsync();

        await app.StartAsync();

        try
        {
            var httpClient = app.CreateHttpClient("mailtriage-api");

            await WaitForHealthyAsync(httpClient, "/alive", maxAttempts: 20);

            var aliveResponse = await httpClient.GetAsync("/alive");
            aliveResponse.StatusCode.Should().Be(HttpStatusCode.OK,
                "API liveness check should succeed");

            var healthResponse = await httpClient.GetAsync("/health");
            healthResponse.StatusCode.Should().Be(HttpStatusCode.OK,
                "API health check should succeed");

            var accountsResponse = await httpClient.GetAsync("/api/accounts");
            accountsResponse.StatusCode.Should().Be(HttpStatusCode.OK,
                "Accounts API should return 200");
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private static async Task WaitForHealthyAsync(HttpClient client, string path, int maxAttempts = 20)
    {
        for (var i = 0; i < maxAttempts; i++)
        {
            try
            {
                var response = await client.GetAsync(path);
                if (response.IsSuccessStatusCode) return;
            }
            catch (HttpRequestException) { /* still starting up */ }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }
}
