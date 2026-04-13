namespace MailTriage.Core.Models;

public class ForwardingRule
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public TriageCategory? MatchCategory { get; set; }
    public TriagePriority? MinPriority { get; set; }
    public string? MatchFromPattern { get; set; }
    public string? MatchSubjectPattern { get; set; }
    public string ForwardToAddress { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
