namespace MailTriage.Core.Models;

/// <summary>
/// Thread-safe snapshot of the polling state for a single mail account.
/// Updated by the background polling loop; read by /api/status.
/// </summary>
public class AccountPollingState
{
    public int AccountId { get; set; }
    public bool IsEnabled { get; set; }
    public string Mailbox { get; set; } = string.Empty;
    public DateTime? LastPollStartedAt { get; set; }
    public DateTime? LastPollCompletedAt { get; set; }

    /// <summary>null until the first poll completes.</summary>
    public bool? LastPollSucceeded { get; set; }

    /// <summary>Safe, non-sensitive error message from the last failed poll.</summary>
    public string? LastError { get; set; }

    /// <summary>Message-Id header of the last email processed, if any.</summary>
    public string? LastMessageIdProcessed { get; set; }
}
