namespace Jellyfin.Plugin.JellyReview.Models;

public class ViewerProfileTagState
{
    public string ViewerProfileId { get; set; } = string.Empty;
    public string State { get; set; } = "pending";
    public string PendingTag { get; set; } = string.Empty;
    public string DeniedTag { get; set; } = string.Empty;
    public string AllowedTag { get; set; } = string.Empty;
}
