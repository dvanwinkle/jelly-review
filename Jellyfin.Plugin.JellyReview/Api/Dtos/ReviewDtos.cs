using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyReview.Api.Dtos;

public class ReviewActionRequest
{
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
    [JsonPropertyName("viewerProfileId")]
    public string? ViewerProfileId { get; set; }
}

public class BulkActionRequest
{
    [JsonPropertyName("itemIds")]
    public List<string> ItemIds { get; set; } = new();
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
    [JsonPropertyName("viewerProfileId")]
    public string? ViewerProfileId { get; set; }
}

public class ReviewDecisionDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    [JsonPropertyName("mediaRecordId")]
    public string MediaRecordId { get; set; } = string.Empty;
    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;
    [JsonPropertyName("decisionReason")]
    public string? DecisionReason { get; set; }
    [JsonPropertyName("reviewerJellyfinUserId")]
    public string? ReviewerJellyfinUserId { get; set; }
    [JsonPropertyName("reviewedAt")]
    public string? ReviewedAt { get; set; }
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}

public class DecisionHistoryDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    [JsonPropertyName("previousState")]
    public string? PreviousState { get; set; }
    [JsonPropertyName("newState")]
    public string NewState { get; set; } = string.Empty;
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;
    [JsonPropertyName("actorType")]
    public string ActorType { get; set; } = string.Empty;
    [JsonPropertyName("actorId")]
    public string? ActorId { get; set; }
    [JsonPropertyName("detailsJson")]
    public string? DetailsJson { get; set; }
    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; } = string.Empty;
}

public class CreateViewerProfileRequest
{
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;
    [JsonPropertyName("jellyfinUserId")]
    public string? JellyfinUserId { get; set; }
    [JsonPropertyName("ageHint")]
    public int? AgeHint { get; set; }
}

public class JellyfinUserDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    [JsonPropertyName("hasProfile")]
    public bool HasProfile { get; set; }
}

public class ViewerProfileDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;
    [JsonPropertyName("jellyfinUserId")]
    public string? JellyfinUserId { get; set; }
    [JsonPropertyName("ageHint")]
    public int? AgeHint { get; set; }
    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }
    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; } = string.Empty;
}
