using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyReview.Models;

public class ReviewRule
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } = 100;
    public string ConditionsJson { get; set; } = "{}";
    public string Action { get; set; } = string.Empty;
    public string? ViewerProfileId { get; set; }
    public List<string> ViewerProfileIds { get; set; } = new();
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
}
