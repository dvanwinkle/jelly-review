namespace Jellyfin.Plugin.JellyReview.Models;

public class SyncCursor
{
    public string Id { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string? CursorValue { get; set; }
    public string? LastRunAt { get; set; }
    public string? LastSuccessAt { get; set; }
    public int ErrorCount { get; set; }
}
