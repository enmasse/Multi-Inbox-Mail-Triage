using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MailTriage.Core.Interfaces;
using MailTriage.Core.Metrics;
using MailTriage.Infrastructure.Data;
using MailTriage.Infrastructure.Imap;
using MailTriage.Infrastructure.Llm;

namespace MailTriage.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMailTriageInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Metrics (singleton – shared across the whole process lifetime)
        services.AddSingleton<IMailTriageMetrics, MailTriageMetrics>();

        // SQLite via EF Core
        var connectionString = configuration.GetConnectionString("DefaultConnection") ?? "Data Source=mailtriage.db";
        services.AddDbContext<MailTriageDbContext>(options =>
            options.UseSqlite(connectionString));

        // Repository
        services.AddScoped<IEmailRepository, EmailRepository>();

        // IMAP
        services.AddSingleton<IImapClientFactory, ImapClientFactory>();
        services.AddScoped<IMailMonitorService, ImapMailMonitorService>();

        // SMTP Forwarder
        services.Configure<SmtpOptions>(o => configuration.GetSection("Smtp").Bind(o));
        services.AddScoped<IEmailForwarder, SmtpEmailForwarder>();

        // Ollama LLM
        services.Configure<OllamaOptions>(o => configuration.GetSection("Ollama").Bind(o));
        services.AddHttpClient<OllamaTriageService>((sp, client) =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OllamaOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
        });
        // Delegate to the typed client registration so the configured HttpClient is used.
        services.AddTransient<ITriageService>(sp => sp.GetRequiredService<OllamaTriageService>());

        return services;
    }
}
