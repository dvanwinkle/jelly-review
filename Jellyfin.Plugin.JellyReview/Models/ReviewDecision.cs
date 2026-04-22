namespace Jellyfin.Plugin.JellyReview.Models;

public class ReviewDecision
{
    public string Id { get; set; } = string.Empty;
    public string MediaRecordId { get; set; } = string.Empty;
    public string State { get; set; } = "pending";
    public string? DecisionReason { get; set; }
    public string? ReviewerJellyfinUserId { get; set; }
    public string? ReviewedAt { get; set; }
    public string Source { get; set; } = "sync_reconciliation";
    public string? Notes { get; set; }
    public bool NeedsResync { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
}
