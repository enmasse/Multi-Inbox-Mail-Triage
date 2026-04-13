namespace MailTriage.Core.Models;

public class TriagedEmail
{
    public long Id { get; set; }
    public int MailAccountId { get; set; }
    public MailAccount MailAccount { get; set; } = null!;
    public string MessageId { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = string.Empty;
    public string ToAddresses { get; set; } = string.Empty;  // JSON array
    public string BodyText { get; set; } = string.Empty;
    public string BodyHtml { get; set; } = string.Empty;
    public DateTime ReceivedAt { get; set; }
    public DateTime TriagedAt { get; set; } = DateTime.UtcNow;
    public TriageCategory Category { get; set; } = TriageCategory.Unknown;
    public TriagePriority Priority { get; set; } = TriagePriority.Normal;
    public string Summary { get; set; } = string.Empty;
    public string ActionRequired { get; set; } = string.Empty;
    public string Labels { get; set; } = string.Empty;  // JSON array
    public bool IsForwarded { get; set; }
    public string? ForwardedTo { get; set; }
    public string RawHeaders { get; set; } = string.Empty;
}
