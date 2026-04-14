using MailTriage.Api.BackgroundServices;
using MailTriage.Api.Services;
using MailTriage.Core.Interfaces;
using MailTriage.Infrastructure;
using MailTriage.Infrastructure.Data;
using MailTriage.Infrastructure.Llm;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Aspire service defaults: OpenTelemetry, health checks, service discovery
builder.AddServiceDefaults();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Infrastructure (IMAP, SQLite, Ollama, SMTP)
builder.Services.AddMailTriageInfrastructure(builder.Configuration);

// Named HTTP client used exclusively by DependencyHealthService for Ollama health probes.
builder.Services.AddHttpClient("OllamaHealth", (sp, client) =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OllamaOptions>>().Value;
    client.BaseAddress = new Uri(opts.BaseUrl);
});

// Polling state store — singleton shared between MailPollingService and StatusController.
builder.Services.AddSingleton<IPollingStateStore, PollingStateStore>();

// Dependency health cache — singleton that caches DB and Ollama connectivity checks.
builder.Services.AddSingleton<DependencyHealthService>();

// Background polling service
builder.Services.AddHostedService<MailPollingService>();

var app = builder.Build();

// Ensure DB is created / migrated
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<MailTriageDbContext>();
    db.Database.EnsureCreated();
}

app.UseHttpsRedirection();
app.MapControllers();
app.MapDefaultEndpoints();

app.Run();

// Make Program accessible for integration tests
public partial class Program { }
