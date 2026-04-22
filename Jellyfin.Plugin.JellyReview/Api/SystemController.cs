using System.Collections.Generic;
using Jellyfin.Api.Constants;
using Jellyfin.Plugin.JellyReview.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyReview.Api;

[ApiController]
[Route("JellyReview/System")]
[Authorize(Policy = Policies.RequiresElevation)]
public class SystemController : ControllerBase
{
    private readonly DatabaseManager _db;

    public SystemController(DatabaseManager db)
    {
        _db = db;
    }

    [HttpGet("status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> GetStatus()
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM media_records WHERE status='active'";
        var mediaCount = (long)(cmd.ExecuteScalar() ?? 0L);

        cmd.CommandText = "SELECT COUNT(*) FROM review_decisions WHERE state='pending'";
        var pendingCount = (long)(cmd.ExecuteScalar() ?? 0L);

        cmd.CommandText = "SELECT last_success_at FROM sync_cursors WHERE source='jellyfin_recent_items' LIMIT 1";
        var lastSync = cmd.ExecuteScalar()?.ToString();

        return Ok(new
        {
            version = Plugin.Instance.Version.ToString(),
            mediaCount,
            pendingCount,
            lastSync
        });
    }
}
