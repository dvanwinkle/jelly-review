using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Api.Constants;
using Jellyfin.Plugin.JellyReview.Api.Dtos;
using Jellyfin.Plugin.JellyReview.Configuration;
using Jellyfin.Plugin.JellyReview.Services;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyReview.Api;

[ApiController]
[Route("JellyReview/Settings")]
[Authorize(Policy = Policies.RequiresElevation)]
public class SettingsController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;
    private readonly ParentalControlService _parentalControlService;
    private readonly SyncService _syncService;

    public SettingsController(
        ILibraryManager libraryManager,
        ParentalControlService parentalControlService,
        SyncService syncService)
    {
        _libraryManager = libraryManager;
        _parentalControlService = parentalControlService;
        _syncService = syncService;
    }

    private PluginConfiguration Config => Plugin.Instance.Configuration;

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<PluginSettingsDto> GetSettings()
    {
        return Ok(new PluginSettingsDto
        {
            PendingTag = Config.PendingTag,
            DeniedTag = Config.DeniedTag,
            AllowedTag = Config.AllowedTag,
            PollingIntervalSeconds = Config.PollingIntervalSeconds,
            AutoRulesEnabled = Config.AutoRulesEnabled,
            SelectedLibraryIds = Config.SelectedLibraryIds
        });
    }

    [HttpPatch("tags")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateTags([FromBody] UpdateTagsRequest req)
    {
        if (!string.IsNullOrWhiteSpace(req.PendingTag))
            Config.PendingTag = req.PendingTag;
        if (!string.IsNullOrWhiteSpace(req.DeniedTag))
            Config.DeniedTag = req.DeniedTag;
        if (!string.IsNullOrWhiteSpace(req.AllowedTag))
            Config.AllowedTag = req.AllowedTag;
        Plugin.Instance.SaveConfiguration();
        await _parentalControlService.ApplyTagPreferencesForActiveProfilesAsync().ConfigureAwait(false);
        await _syncService.ApplyTagsForAllDecisionRecordsAsync().ConfigureAwait(false);
        return NoContent();
    }

    [HttpPatch("libraries")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult UpdateLibraries([FromBody] UpdateLibrariesRequest req)
    {
        Config.SelectedLibraryIds = JsonSerializer.Serialize(req.LibraryIds);
        Plugin.Instance.SaveConfiguration();
        return NoContent();
    }

    [HttpPatch("integrations")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult UpdateIntegrations([FromBody] UpdatePollingRequest req)
    {
        Config.PollingIntervalSeconds = req.PollingIntervalSeconds;
        Config.AutoRulesEnabled = req.AutoRulesEnabled;
        Plugin.Instance.SaveConfiguration();
        return NoContent();
    }

    [HttpGet("libraries/jellyfin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<List<JellyfinLibraryDto>> GetJellyfinLibraries()
    {
        var folders = _libraryManager.GetVirtualFolders();
        var result = new List<JellyfinLibraryDto>();
        foreach (var f in folders)
        {
            result.Add(new JellyfinLibraryDto
            {
                Id = f.ItemId,
                Name = f.Name,
                CollectionType = f.CollectionType?.ToString() ?? string.Empty
            });
        }
        return Ok(result);
    }
}
