using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Api.Constants;
using Jellyfin.Plugin.JellyReview.Api.Dtos;
using Jellyfin.Plugin.JellyReview.Data;
using Jellyfin.Plugin.JellyReview.Services;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyReview.Api;

[ApiController]
[Route("JellyReview/Media")]
[Authorize(Policy = Policies.RequiresElevation)]
public class MediaController : ControllerBase
{
    private readonly DatabaseManager _db;
    private readonly SyncService _syncService;
    private readonly ILibraryManager _libraryManager;

    public MediaController(DatabaseManager db, SyncService syncService, ILibraryManager libraryManager)
    {
        _db = db;
        _syncService = syncService;
        _libraryManager = libraryManager;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<MediaListResponse> GetMedia(
        [FromQuery] string? state,
        [FromQuery] string? search,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 50)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();

        var where = "WHERE mr.status = 'active'";
        if (!string.IsNullOrEmpty(state)) where += " AND rd.state = @state";
        if (!string.IsNullOrEmpty(search)) where += " AND (mr.title LIKE @search OR mr.sort_title LIKE @search)";

        cmd.CommandText = $@"
            SELECT mr.id, mr.jellyfin_item_id, mr.title, mr.sort_title, mr.media_type,
                   mr.year, mr.official_rating, mr.community_rating, mr.runtime_minutes,
                   mr.overview, mr.genres_json, mr.status,
                   rd.id, rd.state, rd.decision_reason, rd.reviewed_at, rd.source, rd.notes,
                   COUNT(*) OVER() as total_count
            FROM media_records mr
            LEFT JOIN review_decisions rd ON rd.media_record_id = mr.id
            {where}
            ORDER BY mr.sort_title, mr.title
            LIMIT @limit OFFSET @offset";

        if (!string.IsNullOrEmpty(state)) cmd.Parameters.AddWithValue("@state", state);
        if (!string.IsNullOrEmpty(search)) cmd.Parameters.AddWithValue("@search", $"%{search}%");
        cmd.Parameters.AddWithValue("@limit", limit);
        cmd.Parameters.AddWithValue("@offset", offset);

        var items = new List<MediaItemDto>();
        int total = 0;

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            total = r.GetInt32(18);
            var genres = new List<string>();
            if (!r.IsDBNull(10))
            {
                try { genres = JsonSerializer.Deserialize<List<string>>(r.GetString(10)) ?? new(); }
                catch { }
            }

            var dto = new MediaItemDto
            {
                Id = r.GetString(0),
                JellyfinItemId = r.GetString(1),
                Title = r.GetString(2),
                SortTitle = r.IsDBNull(3) ? null : r.GetString(3),
                MediaType = r.GetString(4),
                Year = r.IsDBNull(5) ? null : r.GetInt32(5),
                OfficialRating = r.IsDBNull(6) ? null : r.GetString(6),
                CommunityRating = r.IsDBNull(7) ? null : r.GetDouble(7),
                RuntimeMinutes = r.IsDBNull(8) ? null : r.GetInt32(8),
                Overview = r.IsDBNull(9) ? null : r.GetString(9),
                Genres = genres,
                Status = r.GetString(11),
            };

            if (!r.IsDBNull(12))
            {
                dto.Decision = new ReviewDecisionDto
                {
                    Id = r.GetString(12),
                    MediaRecordId = r.GetString(0),
                    State = r.GetString(13),
                    DecisionReason = r.IsDBNull(14) ? null : r.GetString(14),
                    ReviewedAt = r.IsDBNull(15) ? null : r.GetString(15),
                    Source = r.GetString(16),
                    Notes = r.IsDBNull(17) ? null : r.GetString(17),
                };
            }

            items.Add(dto);
        }

        return Ok(new MediaListResponse { Items = items, Total = total, Offset = offset, Limit = limit });
    }

    [HttpGet("counts")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<MediaCountsDto> GetCounts()
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT rd.state, COUNT(*) as cnt
            FROM media_records mr
            INNER JOIN review_decisions rd ON rd.media_record_id = mr.id
            WHERE mr.status = 'active'
            GROUP BY rd.state";

        var counts = new MediaCountsDto();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var s = r.GetString(0);
            var n = r.GetInt32(1);
            switch (s)
            {
                case "pending": counts.Pending = n; break;
                case "approved": counts.Approved = n; break;
                case "denied": counts.Denied = n; break;
                case "deferred": counts.Deferred = n; break;
            }
        }
        counts.Total = counts.Pending + counts.Approved + counts.Denied + counts.Deferred;
        return Ok(counts);
    }

    [HttpGet("{itemId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<MediaItemDto> GetItem(string itemId)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT mr.id, mr.jellyfin_item_id, mr.title, mr.sort_title, mr.media_type,
                   mr.year, mr.official_rating, mr.community_rating, mr.runtime_minutes,
                   mr.overview, mr.genres_json, mr.status,
                   rd.id, rd.state, rd.decision_reason, rd.reviewed_at, rd.source, rd.notes
            FROM media_records mr
            LEFT JOIN review_decisions rd ON rd.media_record_id = mr.id
            WHERE mr.id = @id OR mr.jellyfin_item_id = @id";
        cmd.Parameters.AddWithValue("@id", itemId);

        using var r = cmd.ExecuteReader();
        if (!r.Read()) return NotFound();

        var genres = new List<string>();
        if (!r.IsDBNull(10))
        {
            try { genres = JsonSerializer.Deserialize<List<string>>(r.GetString(10)) ?? new(); }
            catch { }
        }

        var dto = new MediaItemDto
        {
            Id = r.GetString(0), JellyfinItemId = r.GetString(1), Title = r.GetString(2),
            SortTitle = r.IsDBNull(3) ? null : r.GetString(3), MediaType = r.GetString(4),
            Year = r.IsDBNull(5) ? null : r.GetInt32(5),
            OfficialRating = r.IsDBNull(6) ? null : r.GetString(6),
            CommunityRating = r.IsDBNull(7) ? null : r.GetDouble(7),
            RuntimeMinutes = r.IsDBNull(8) ? null : r.GetInt32(8),
            Overview = r.IsDBNull(9) ? null : r.GetString(9),
            Genres = genres, Status = r.GetString(11),
        };
        if (!r.IsDBNull(12))
        {
            dto.Decision = new ReviewDecisionDto
            {
                Id = r.GetString(12), MediaRecordId = r.GetString(0), State = r.GetString(13),
                DecisionReason = r.IsDBNull(14) ? null : r.GetString(14),
                ReviewedAt = r.IsDBNull(15) ? null : r.GetString(15),
                Source = r.GetString(16), Notes = r.IsDBNull(17) ? null : r.GetString(17),
            };
        }

        return Ok(dto);
    }

    [HttpGet("{itemId}/poster")]
    [AllowAnonymous]
    public ActionResult GetPoster(string itemId)
    {
        // Resolve Jellyfin item ID from media record
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT jellyfin_item_id FROM media_records WHERE id = @id OR jellyfin_item_id = @id LIMIT 1";
        cmd.Parameters.AddWithValue("@id", itemId);
        var jellyfinId = cmd.ExecuteScalar()?.ToString();

        if (string.IsNullOrEmpty(jellyfinId)) return NotFound();

        // Redirect to Jellyfin's own image service — no proxy needed, same host
        var imageUrl = $"/Items/{jellyfinId}/Images/Primary";
        return Redirect(imageUrl);
    }

    [HttpPost("sync")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<SyncResultDto>> Sync(
        [FromQuery] bool full = false,
        CancellationToken cancellationToken = default)
    {
        SyncResult result;
        if (full)
            result = await _syncService.RunFullImportAsync(cancellationToken: cancellationToken);
        else
            result = await _syncService.RunIncrementalSyncAsync(cancellationToken);

        return Ok(new SyncResultDto
        {
            Imported = result.Imported,
            Updated = result.Updated,
            Deleted = result.Deleted,
            Errors = result.Errors
        });
    }

    [HttpPost("reconcile")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<SyncResultDto>> Reconcile(CancellationToken cancellationToken = default)
    {
        var result = await _syncService.RunFullImportAsync(cancellationToken: cancellationToken);
        return Ok(new SyncResultDto
        {
            Imported = result.Imported, Updated = result.Updated,
            Deleted = result.Deleted, Errors = result.Errors
        });
    }
}
