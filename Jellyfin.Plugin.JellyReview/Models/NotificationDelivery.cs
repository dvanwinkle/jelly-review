namespace Jellyfin.Plugin.JellyReview.Models;

public class NotificationDelivery
{
    public string Id { get; set; } = string.Empty;
    public string ChannelId { get; set; } = string.Empty;
    public string? MediaRecordId { get; set; }
    public string TriggerEvent { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public string? ProviderMessageId { get; set; }
    public string? ActionTokenHash { get; set; }
    public string? ActionedAt { get; set; }
    public string? ErrorDetail { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
}
