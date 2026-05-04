using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Events;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JellyReview.Services;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyReview.ScheduledTasks;

// Triggers a user sync whenever Jellyfin fires OnUserUpdated. Combined with the daily
// UserSyncTask this gives near-immediate cleanup: Jellyfin has no OnUserDeleted event
// in the plugin API, but any user management activity after a deletion will cause
// orphaned profiles to be caught and deactivated.
public class UserEventListener : IHostedService
{
    private readonly IUserManager _userManager;
    private readonly UserSyncService _userSyncService;
    private readonly ILogger<UserEventListener> _logger;

    public UserEventListener(
        IUserManager userManager,
        UserSyncService userSyncService,
        ILogger<UserEventListener> logger)
    {
        _userManager = userManager;
        _userSyncService = userSyncService;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _userManager.OnUserUpdated += OnUserUpdated;
        _logger.LogInformation("JellyReview: user event listener started");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _userManager.OnUserUpdated -= OnUserUpdated;
        return Task.CompletedTask;
    }

    // async void is intentional — Jellyfin's event system does not await handlers.
    private async void OnUserUpdated(object? sender, GenericEventArgs<User> e)
    {
        try
        {
            await _userSyncService.SyncDeletedUsersAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JellyReview: error during user sync triggered by OnUserUpdated");
        }
    }
}
