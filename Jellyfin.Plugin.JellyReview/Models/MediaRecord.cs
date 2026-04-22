namespace Jellyfin.Plugin.JellyReview.Models;

public class MediaRecord
{
    public string Id { get; set; } = string.Empty;
    public string JellyfinItemId { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? SortTitle { get; set; }
    public int? Year { get; set; }
    public string? OfficialRating { get; set; }
    public double? CommunityRating { get; set; }
    public int? RuntimeMinutes { get; set; }
    public string? Overview { get; set; }
    public string? GenresJson { get; set; }
    public string? TagsSnapshotJson { get; set; }
    public string Status { get; set; } = "active";
    public string? MetadataHash { get; set; }
    public string FirstSeenAt { get; set; } = string.Empty;
    public string LastSeenAt { get; set; } = string.Empty;
    public string? LastSyncedAt { get; set; }
}
