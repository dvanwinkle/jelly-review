using System.Threading.Tasks;
using Jellyfin.Plugin.JellyReview.Data;
using Jellyfin.Plugin.JellyReview.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyReview.Api;

/// One-click approve/deny/defer from notification links.
/// AllowAnonymous because the HMAC token is the credential — no session cookie needed.
[ApiController]
[Route("JellyReview/Action")]
[AllowAnonymous]
public class ActionTokenController : ControllerBase
{
    private readonly NotificationService _notifications;
    private readonly ReviewService _reviewService;
    private readonly SyncService _syncService;
    private readonly DatabaseManager _db;

    public ActionTokenController(
        NotificationService notifications,
        ReviewService reviewService,
        SyncService syncService,
        DatabaseManager db)
    {
        _notifications = notifications;
        _reviewService = reviewService;
        _syncService = syncService;
        _db = db;
    }

    [HttpPost("{token}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    public async Task<ActionResult<object>> HandleToken(string token)
    {
        var (mediaRecordId, action, viewerProfileId) = _notifications.VerifyActionToken(token);

        if (mediaRecordId == null || action == null)
            return StatusCode(StatusCodes.Status410Gone, new { error = "Token is invalid or expired" });

        var validActions = new[] { "approve", "deny", "defer" };
        if (!System.Array.Exists(validActions, a => a == action))
            return BadRequest(new { error = "Invalid action in token" });

        Models.ReviewDecision? decision;
        if (viewerProfileId != null)
        {
            await _reviewService.ApplyViewerActionAsync(
                mediaRecordId, viewerProfileId, action, "system", null, null, null, "action_token");
            decision = _reviewService.GetDecision(mediaRecordId);
        }
        else
        {
            decision = await _reviewService.ApplyActionAsync(
                mediaRecordId, action, "system", null, null, null, "action_token");
        }

        if (decision == null)
            return NotFound(new { error = "Media item not found" });

        // Apply tags
        var jellyfinId = GetJellyfinItemId(mediaRecordId);
        if (!string.IsNullOrEmpty(jellyfinId))
            await _syncService.ApplyTagsForItemAsync(mediaRecordId);

        return Ok(new { success = true, action, state = decision.State });
    }

    private string? GetJellyfinItemId(string mediaRecordId)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT jellyfin_item_id FROM media_records WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", mediaRecordId);
        return cmd.ExecuteScalar()?.ToString();
    }
}
