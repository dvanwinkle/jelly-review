using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyReview.Api.Dtos;

public class PluginSettingsDto
{
    [JsonPropertyName("pendingTag")]
    public string PendingTag { get; set; } = string.Empty;
    [JsonPropertyName("deniedTag")]
    public string DeniedTag { get; set; } = string.Empty;
    [JsonPropertyName("pollingIntervalSeconds")]
    public int PollingIntervalSeconds { get; set; }
    [JsonPropertyName("autoRulesEnabled")]
    public bool AutoRulesEnabled { get; set; }
    [JsonPropertyName("selectedLibraryIds")]
    public string SelectedLibraryIds { get; set; } = string.Empty;
}

public class UpdateTagsRequest
{
    [JsonPropertyName("pendingTag")]
    public string? PendingTag { get; set; }
    [JsonPropertyName("deniedTag")]
    public string? DeniedTag { get; set; }
}

public class UpdateLibrariesRequest
{
    [JsonPropertyName("libraryIds")]
    public List<string> LibraryIds { get; set; } = new();
}

public class UpdatePollingRequest
{
    [JsonPropertyName("pollingIntervalSeconds")]
    public int PollingIntervalSeconds { get; set; }
    [JsonPropertyName("autoRulesEnabled")]
    public bool AutoRulesEnabled { get; set; }
}

public class JellyfinLibraryDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    [JsonPropertyName("collectionType")]
    public string CollectionType { get; set; } = string.Empty;
}
