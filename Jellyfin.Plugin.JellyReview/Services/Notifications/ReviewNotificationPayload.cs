using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyReview.Services.Notifications;

public class ReviewNotificationPayload
{
    public string MediaRecordId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int? Year { get; set; }
    public string MediaType { get; set; } = string.Empty;
    public string? OfficialRating { get; set; }
    public string? Overview { get; set; }
    public string PosterProxyUrl { get; set; } = string.Empty;
    public List<string> Genres { get; set; } = new();
    public string? RequestedBy { get; set; }
    public int PendingCount { get; set; }
    public string ApproveTokenUrl { get; set; } = string.Empty;
    public string DenyTokenUrl { get; set; } = string.Empty;
    public string DeferTokenUrl { get; set; } = string.Empty;
    public string DetailUrl { get; set; } = string.Empty;
}

public class DeliveryResult
{
    public bool Success { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? Error { get; set; }
}
