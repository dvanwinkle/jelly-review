using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyReview.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyReview.Services;

public class TagManager
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<TagManager> _logger;

    // Re-entrancy guard: UpdateItemAsync re-triggers ItemAdded — without this, infinite loop.
    private static readonly HashSet<Guid> _tagWriteInFlight = new();
    private static readonly SemaphoreSlim _tagLock = new(1, 1);

    public TagManager(ILibraryManager libraryManager, ILogger<TagManager> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    private PluginConfiguration Config => Plugin.Instance.Configuration;

    public bool IsTagWriteInFlight(Guid itemId)
    {
        _tagLock.Wait();
        try { return _tagWriteInFlight.Contains(itemId); }
        finally { _tagLock.Release(); }
    }

    public async Task ApplyPendingTagAsync(Guid itemId)
    {
        await _tagLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_tagWriteInFlight.Contains(itemId)) return;
            _tagWriteInFlight.Add(itemId);
        }
        finally
        {
            _tagLock.Release();
        }

        try
        {
            var item = _libraryManager.GetItemById(itemId);
            if (item == null) return;

            var tags = item.Tags?.ToList() ?? new List<string>();
            if (!tags.Contains(Config.PendingTag))
            {
                tags.Add(Config.PendingTag);
                item.Tags = tags.ToArray();
                await _libraryManager.UpdateItemAsync(
                    item, item.GetParent(), ItemUpdateType.MetadataEdit, CancellationToken.None)
                    .ConfigureAwait(false);
                _logger.LogDebug("JellyReview: applied pending tag to {ItemId}", itemId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JellyReview: failed to apply pending tag to {ItemId}", itemId);
        }
        finally
        {
            await _tagLock.WaitAsync().ConfigureAwait(false);
            _tagWriteInFlight.Remove(itemId);
            _tagLock.Release();
        }
    }

    public async Task ApplyDecisionTagsAsync(Guid itemId, string state)
    {
        await _tagLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_tagWriteInFlight.Contains(itemId)) return;
            _tagWriteInFlight.Add(itemId);
        }
        finally
        {
            _tagLock.Release();
        }

        try
        {
            var item = _libraryManager.GetItemById(itemId);
            if (item == null) return;

            var tags = item.Tags?.ToList() ?? new List<string>();

            switch (state)
            {
                case "approved":
                    tags.Remove(Config.PendingTag);
                    tags.Remove(Config.DeniedTag);
                    break;
                case "denied":
                    tags.Remove(Config.PendingTag);
                    if (!tags.Contains(Config.DeniedTag))
                        tags.Add(Config.DeniedTag);
                    break;
                case "pending":
                    if (!tags.Contains(Config.PendingTag))
                        tags.Add(Config.PendingTag);
                    tags.Remove(Config.DeniedTag);
                    break;
                case "deferred":
                    // keep PendingTag so child cannot see until final decision
                    if (!tags.Contains(Config.PendingTag))
                        tags.Add(Config.PendingTag);
                    tags.Remove(Config.DeniedTag);
                    break;
                default:
                    return;
            }

            item.Tags = tags.ToArray();
            await _libraryManager.UpdateItemAsync(
                item, item.GetParent(), ItemUpdateType.MetadataEdit, CancellationToken.None)
                .ConfigureAwait(false);

            _logger.LogDebug("JellyReview: applied {State} tags to {ItemId}", state, itemId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JellyReview: failed to apply decision tags to {ItemId}", itemId);
        }
        finally
        {
            await _tagLock.WaitAsync().ConfigureAwait(false);
            _tagWriteInFlight.Remove(itemId);
            _tagLock.Release();
        }
    }

    public async Task RemoveAllJellyReviewTagsAsync(Guid itemId)
    {
        await _tagLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_tagWriteInFlight.Contains(itemId)) return;
            _tagWriteInFlight.Add(itemId);
        }
        finally
        {
            _tagLock.Release();
        }

        try
        {
            var item = _libraryManager.GetItemById(itemId);
            if (item == null) return;

            var tags = (item.Tags ?? Array.Empty<string>())
                .Where(t => t != Config.PendingTag && t != Config.DeniedTag)
                .ToArray();

            item.Tags = tags;
            await _libraryManager.UpdateItemAsync(
                item, item.GetParent(), ItemUpdateType.MetadataEdit, CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            await _tagLock.WaitAsync().ConfigureAwait(false);
            _tagWriteInFlight.Remove(itemId);
            _tagLock.Release();
        }
    }
}
