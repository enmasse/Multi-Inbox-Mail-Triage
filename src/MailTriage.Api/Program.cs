using MailTriage.Api.BackgroundServices;
using MailTriage.Infrastructure;
using MailTriage.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Aspire service defaults: OpenTelemetry, health checks, service discovery
builder.AddServiceDefaults();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Infrastructure (IMAP, SQLite, Ollama, SMTP)
builder.Services.AddMailTriageInfrastructure(builder.Configuration);

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
