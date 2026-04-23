using System;
using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Jellyfin.Plugin.JellyReview.Configuration;

namespace Jellyfin.Plugin.JellyReview;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public static Plugin Instance { get; private set; } = null!;

    public Plugin(IApplicationPaths paths, IXmlSerializer serializer)
        : base(paths, serializer)
    {
        Instance = this;
    }

    public override string Name => "JellyReview";

    // Stable GUID — never change after first release
    public override Guid Id => Guid.Parse("a8b1c2d3-e4f5-6789-abcd-ef0123456789");

    public IEnumerable<PluginPageInfo> GetPages() => new[]
    {
        new PluginPageInfo
        {
            Name = "JellyReview",
            EmbeddedResourcePath = $"{GetType().Namespace}.Web.Pages.dashboard.html",
            EnableInMainMenu = true,
            DisplayName = "JellyReview"
        },
        new PluginPageInfo
        {
            Name = "jellyreview-dashboard.js",
            EmbeddedResourcePath = $"{GetType().Namespace}.Web.Scripts.dashboard.js"
        },
        new PluginPageInfo
        {
            Name = "JellyReviewConfig",
            EmbeddedResourcePath = $"{GetType().Namespace}.Web.Pages.config.html"
        }
    };
}
