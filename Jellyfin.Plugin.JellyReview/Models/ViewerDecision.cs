namespace Jellyfin.Plugin.JellyReview.Models;

public class ViewerDecision
{
    public string Id { get; set; } = string.Empty;
    public string ViewerProfileId { get; set; } = string.Empty;
    public string MediaRecordId { get; set; } = string.Empty;
    public string State { get; set; } = "pending";
    public string? DecisionReason { get; set; }
    public string? ReviewerJellyfinUserId { get; set; }
    public string? ReviewedAt { get; set; }
    public string Source { get; set; } = "manual_review";
    public string? Notes { get; set; }
    public bool NeedsResync { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
}
