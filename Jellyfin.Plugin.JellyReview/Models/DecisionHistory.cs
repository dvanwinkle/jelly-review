namespace Jellyfin.Plugin.JellyReview.Models;

public class DecisionHistory
{
    public string Id { get; set; } = string.Empty;
    public string MediaRecordId { get; set; } = string.Empty;
    public string? ViewerProfileId { get; set; }
    public string? PreviousState { get; set; }
    public string NewState { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string ActorType { get; set; } = string.Empty;
    public string? ActorId { get; set; }
    public string? DetailsJson { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
}
