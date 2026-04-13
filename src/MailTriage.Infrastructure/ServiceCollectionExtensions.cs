using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MailTriage.Core.Interfaces;
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
            var options = configuration.GetSection("Ollama").Get<OllamaOptions>() ?? new OllamaOptions();
            client.BaseAddress = new Uri(options.BaseUrl);
        });
        services.AddScoped<ITriageService, OllamaTriageService>();

        return services;
    }
}
