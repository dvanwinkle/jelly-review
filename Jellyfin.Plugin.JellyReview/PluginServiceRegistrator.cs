using Jellyfin.Plugin.JellyReview.Data;
using Jellyfin.Plugin.JellyReview.ScheduledTasks;
using Jellyfin.Plugin.JellyReview.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.JellyReview;

public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<DatabaseManager>();
        serviceCollection.AddSingleton<TagManager>();
        serviceCollection.AddSingleton<RuleEngine>();
        serviceCollection.AddSingleton<ReviewService>();
        serviceCollection.AddSingleton<NotificationService>();
        serviceCollection.AddSingleton<SyncService>();
        serviceCollection.AddHostedService<LibraryEventListener>();
        serviceCollection.AddScoped<IncrementalSyncTask>();
    }
}
