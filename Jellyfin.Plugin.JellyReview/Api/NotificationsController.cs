using System.Collections.Generic;
using System.Threading.Tasks;
using Jellyfin.Api.Constants;
using Jellyfin.Plugin.JellyReview.Api.Dtos;
using Jellyfin.Plugin.JellyReview.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyReview.Api;

[ApiController]
[Route("JellyReview/Notifications")]
[Authorize(Policy = Policies.RequiresElevation)]
public class NotificationsController : ControllerBase
{
    private readonly NotificationService _notifications;

    public NotificationsController(NotificationService notifications)
    {
        _notifications = notifications;
    }

    [HttpGet("channels")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<List<NotificationChannelDto>> GetChannels()
    {
        var channels = _notifications.GetChannels();
        var dtos = new List<NotificationChannelDto>();
        foreach (var ch in channels)
        {
            dtos.Add(new NotificationChannelDto
            {
                Id = ch.Id, Name = ch.Name, ProviderType = ch.ProviderType,
                Enabled = ch.Enabled, NotifyOnPending = ch.NotifyOnPending,
                NotifyOnConflict = ch.NotifyOnConflict, CreatedAt = ch.CreatedAt
            });
        }
        return Ok(dtos);
    }

    [HttpPost("channels")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult<NotificationChannelDto>> CreateChannel([FromBody] CreateChannelRequest req)
    {
        var channel = await _notifications.CreateChannelAsync(
            req.Name, req.ProviderType, req.Config,
            req.NotifyOnPending, req.NotifyOnConflict);

        var dto = new NotificationChannelDto
        {
            Id = channel.Id, Name = channel.Name, ProviderType = channel.ProviderType,
            Enabled = channel.Enabled, NotifyOnPending = channel.NotifyOnPending,
            NotifyOnConflict = channel.NotifyOnConflict, CreatedAt = channel.CreatedAt
        };
        return CreatedAtAction(nameof(GetChannels), dto);
    }

    [HttpPatch("channels/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateChannel(string id, [FromBody] UpdateChannelRequest req)
    {
        var existing = _notifications.GetChannel(id);
        if (existing == null) return NotFound();

        await _notifications.UpdateChannelAsync(id, req.Name, req.Enabled, req.Config,
            req.NotifyOnPending, req.NotifyOnConflict);
        return NoContent();
    }

    [HttpDelete("channels/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteChannel(string id)
    {
        await _notifications.DeleteChannelAsync(id);
        return NoContent();
    }

    [HttpPost("channels/{id}/test")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<TestChannelResult>> TestChannel(string id)
    {
        var result = await _notifications.TestChannelAsync(id);
        return Ok(new TestChannelResult { Success = result.Success, Error = result.Error });
    }
}
