using System.Collections.Generic;
using System.Text.Json;
using Jellyfin.Api.Constants;
using Jellyfin.Plugin.JellyReview.Api.Dtos;
using Jellyfin.Plugin.JellyReview.Configuration;
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

    public SettingsController(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
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
            PollingIntervalSeconds = Config.PollingIntervalSeconds,
            AutoRulesEnabled = Config.AutoRulesEnabled,
            SelectedLibraryIds = Config.SelectedLibraryIds
        });
    }

    [HttpPatch("tags")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult UpdateTags([FromBody] UpdateTagsRequest req)
    {
        if (!string.IsNullOrWhiteSpace(req.PendingTag))
            Config.PendingTag = req.PendingTag;
        if (!string.IsNullOrWhiteSpace(req.DeniedTag))
            Config.DeniedTag = req.DeniedTag;
        Plugin.Instance.SaveConfiguration();
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
