namespace Jellyfin.Plugin.JellyReview.Models;

public class ViewerProfile
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? JellyfinUserId { get; set; }
    public int? AgeHint { get; set; }
    public string? ActiveRuleSetId { get; set; }
    public string? PendingTag { get; set; }
    public string? DeniedTag { get; set; }
    public string? AllowedTag { get; set; }
    public string CreatedByJellyfinUserId { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
}
