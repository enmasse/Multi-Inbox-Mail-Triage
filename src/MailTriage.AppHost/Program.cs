// MailTriage Aspire AppHost
// Orchestrates the MailTriage.Api service and its dependencies for local development.
// Run `dotnet run --project src/MailTriage.AppHost` to start the full stack.

var builder = DistributedApplication.CreateBuilder(args);

// MailTriage API service
// When running locally, the SQLite database is created in the API's working directory.
// Configure Ollama and SMTP via environment or appsettings.
var api = builder.AddProject<Projects.MailTriage_Api>("mailtriage-api")
    .WithExternalHttpEndpoints();

builder.Build().Run();
