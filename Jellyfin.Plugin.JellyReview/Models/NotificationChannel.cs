namespace Jellyfin.Plugin.JellyReview.Models;

public class NotificationChannel
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ProviderType { get; set; } = string.Empty;
    public string? ConfigJson { get; set; }
    public bool Enabled { get; set; } = true;
    public bool NotifyOnPending { get; set; } = true;
    public bool NotifyOnConflict { get; set; } = true;
    public bool NotifyOnDigest { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
}
