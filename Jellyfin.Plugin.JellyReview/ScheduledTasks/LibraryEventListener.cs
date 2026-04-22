using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyReview.Services;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyReview.ScheduledTasks;

public class LibraryEventListener : IHostedService
{
    private readonly ILibraryManager _libraryManager;
    private readonly SyncService _syncService;
    private readonly TagManager _tagManager;
    private readonly ILogger<LibraryEventListener> _logger;

    public LibraryEventListener(
        ILibraryManager libraryManager,
        SyncService syncService,
        TagManager tagManager,
        ILogger<LibraryEventListener> logger)
    {
        _libraryManager = libraryManager;
        _syncService = syncService;
        _tagManager = tagManager;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded += OnItemAdded;
        _logger.LogInformation("JellyReview: library event listener started");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnItemAdded;
        return Task.CompletedTask;
    }

    // async void is intentional — Jellyfin's event does not await handlers.
    private async void OnItemAdded(object? sender, ItemChangeEventArgs e)
    {
        // Only process top-level Movie and Series — Jellyfin fires ItemAdded for Episodes too.
        if (e.Item is not (Movie or Series)) return;

        // Skip writes triggered by our own tag operations to avoid infinite re-entry.
        if (_tagManager.IsTagWriteInFlight(e.Item.Id)) return;

        try
        {
            await _syncService.HandleNewItemAsync(e.Item).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JellyReview: unhandled error processing ItemAdded for {ItemId}", e.Item.Id);
        }
    }
}
