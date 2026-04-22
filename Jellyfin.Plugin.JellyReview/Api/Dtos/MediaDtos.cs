using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyReview.Api.Dtos;

public class MediaItemDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    [JsonPropertyName("jellyfinItemId")]
    public string JellyfinItemId { get; set; } = string.Empty;
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;
    [JsonPropertyName("sortTitle")]
    public string? SortTitle { get; set; }
    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = string.Empty;
    [JsonPropertyName("year")]
    public int? Year { get; set; }
    [JsonPropertyName("officialRating")]
    public string? OfficialRating { get; set; }
    [JsonPropertyName("communityRating")]
    public double? CommunityRating { get; set; }
    [JsonPropertyName("runtimeMinutes")]
    public int? RuntimeMinutes { get; set; }
    [JsonPropertyName("overview")]
    public string? Overview { get; set; }
    [JsonPropertyName("genres")]
    public List<string> Genres { get; set; } = new();
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
    [JsonPropertyName("decision")]
    public ReviewDecisionDto? Decision { get; set; }
}

public class MediaListResponse
{
    [JsonPropertyName("items")]
    public List<MediaItemDto> Items { get; set; } = new();
    [JsonPropertyName("total")]
    public int Total { get; set; }
    [JsonPropertyName("offset")]
    public int Offset { get; set; }
    [JsonPropertyName("limit")]
    public int Limit { get; set; }
}

public class MediaCountsDto
{
    [JsonPropertyName("pending")]
    public int Pending { get; set; }
    [JsonPropertyName("approved")]
    public int Approved { get; set; }
    [JsonPropertyName("denied")]
    public int Denied { get; set; }
    [JsonPropertyName("deferred")]
    public int Deferred { get; set; }
    [JsonPropertyName("total")]
    public int Total { get; set; }
}

public class SyncResultDto
{
    [JsonPropertyName("imported")]
    public int Imported { get; set; }
    [JsonPropertyName("updated")]
    public int Updated { get; set; }
    [JsonPropertyName("deleted")]
    public int Deleted { get; set; }
    [JsonPropertyName("errors")]
    public int Errors { get; set; }
}
