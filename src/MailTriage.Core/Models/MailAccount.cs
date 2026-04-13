namespace MailTriage.Core.Models;

public class MailAccount
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 993;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool UseSsl { get; set; } = true;
    public bool IsEnabled { get; set; } = true;
    public string Mailbox { get; set; } = "INBOX";
    public int PollingIntervalSeconds { get; set; } = 60;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<TriagedEmail> TriagedEmails { get; set; } = new List<TriagedEmail>();
}
