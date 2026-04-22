using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyReview.Api.Dtos;

public class NotificationChannelDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    [JsonPropertyName("providerType")]
    public string ProviderType { get; set; } = string.Empty;
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }
    [JsonPropertyName("notifyOnPending")]
    public bool NotifyOnPending { get; set; }
    [JsonPropertyName("notifyOnConflict")]
    public bool NotifyOnConflict { get; set; }
    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; } = string.Empty;
}

public class CreateChannelRequest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    [JsonPropertyName("providerType")]
    public string ProviderType { get; set; } = string.Empty;
    [JsonPropertyName("config")]
    public Dictionary<string, object> Config { get; set; } = new();
    [JsonPropertyName("notifyOnPending")]
    public bool NotifyOnPending { get; set; } = true;
    [JsonPropertyName("notifyOnConflict")]
    public bool NotifyOnConflict { get; set; } = true;
}

public class UpdateChannelRequest
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }
    [JsonPropertyName("config")]
    public Dictionary<string, object>? Config { get; set; }
    [JsonPropertyName("notifyOnPending")]
    public bool? NotifyOnPending { get; set; }
    [JsonPropertyName("notifyOnConflict")]
    public bool? NotifyOnConflict { get; set; }
}

public class TestChannelResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
