using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellyReview.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public string PendingTag { get; set; } = "jelly-guard-pending";

    public string DeniedTag { get; set; } = "jelly-guard-denied";

    public int PollingIntervalSeconds { get; set; } = 300;

    public bool AutoRulesEnabled { get; set; } = true;

    // JSON array of library GUIDs, e.g. ["guid1","guid2"]. Empty string = all libraries.
    public string SelectedLibraryIds { get; set; } = string.Empty;
}
