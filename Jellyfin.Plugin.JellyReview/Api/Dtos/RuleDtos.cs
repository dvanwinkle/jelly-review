using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyReview.Api.Dtos;

public class ReviewRuleDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }
    [JsonPropertyName("priority")]
    public int Priority { get; set; }
    [JsonPropertyName("conditionsJson")]
    public string ConditionsJson { get; set; } = "{}";
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;
    [JsonPropertyName("viewerProfileId")]
    public string? ViewerProfileId { get; set; }
    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; } = string.Empty;
}

public class CreateRuleRequest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 100;
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
    [JsonPropertyName("conditionsJson")]
    public string ConditionsJson { get; set; } = "{}";
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;
    [JsonPropertyName("viewerProfileId")]
    public string? ViewerProfileId { get; set; }
}

public class UpdateRuleRequest
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    [JsonPropertyName("priority")]
    public int? Priority { get; set; }
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }
    [JsonPropertyName("conditionsJson")]
    public string? ConditionsJson { get; set; }
    [JsonPropertyName("action")]
    public string? Action { get; set; }
}

public class EvaluateRuleRequest
{
    [JsonPropertyName("jellyfinItemId")]
    public string JellyfinItemId { get; set; } = string.Empty;
    [JsonPropertyName("viewerProfileId")]
    public string? ViewerProfileId { get; set; }
}

public class EvaluateRuleResult
{
    [JsonPropertyName("action")]
    public string? Action { get; set; }
    [JsonPropertyName("matchedRuleName")]
    public string? MatchedRuleName { get; set; }
    [JsonPropertyName("matchedRuleId")]
    public string? MatchedRuleId { get; set; }
}
