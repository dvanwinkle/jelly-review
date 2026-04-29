using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Jellyfin.Api.Constants;
using Jellyfin.Plugin.JellyReview.Api.Dtos;
using Jellyfin.Plugin.JellyReview.Data;
using Jellyfin.Plugin.JellyReview.Services;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

namespace Jellyfin.Plugin.JellyReview.Api;

[ApiController]
[Route("JellyReview/Reviews")]
[Authorize(Policy = Policies.RequiresElevation)]
public class ReviewController : ControllerBase
{
    private readonly ReviewService _reviewService;
    private readonly SyncService _syncService;
    private readonly DatabaseManager _db;
    private readonly IUserManager _userManager;
    private readonly ParentalControlService _parentalControlService;

    public ReviewController(
        ReviewService reviewService,
        SyncService syncService,
        DatabaseManager db,
        IUserManager userManager,
        ParentalControlService parentalControlService)
    {
        _reviewService = reviewService;
        _syncService = syncService;
        _db = db;
        _userManager = userManager;
        _parentalControlService = parentalControlService;
    }

    [HttpPost("{itemId}/approve")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReviewDecisionDto>> Approve(string itemId, [FromBody] ReviewActionRequest? req)
    {
        var mediaRecordId = ResolveMediaRecordId(itemId);
        if (mediaRecordId == null) return NotFound();

        var actorId = User.FindFirst("sub")?.Value ?? User.FindFirst("id")?.Value;
        var decision = await ApplyReviewActionAsync(mediaRecordId, "approve", req, actorId);
        if (decision == null) return NotFound();

        await _syncService.ApplyTagsForItemAsync(mediaRecordId);
        return Ok(MapDecision(decision));
    }

    [HttpPost("{itemId}/deny")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReviewDecisionDto>> Deny(string itemId, [FromBody] ReviewActionRequest? req)
    {
        var mediaRecordId = ResolveMediaRecordId(itemId);
        if (mediaRecordId == null) return NotFound();

        var actorId = User.FindFirst("sub")?.Value ?? User.FindFirst("id")?.Value;
        var decision = await ApplyReviewActionAsync(mediaRecordId, "deny", req, actorId);
        if (decision == null) return NotFound();

        await _syncService.ApplyTagsForItemAsync(mediaRecordId);
        return Ok(MapDecision(decision));
    }

    [HttpPost("{itemId}/defer")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<ReviewDecisionDto>> Defer(string itemId, [FromBody] ReviewActionRequest? req)
    {
        var mediaRecordId = ResolveMediaRecordId(itemId);
        if (mediaRecordId == null) return NotFound();

        var actorId = User.FindFirst("sub")?.Value ?? User.FindFirst("id")?.Value;
        var decision = await ApplyReviewActionAsync(mediaRecordId, "defer", req, actorId);
        if (decision == null) return NotFound();

        await _syncService.ApplyTagsForItemAsync(mediaRecordId);
        return Ok(MapDecision(decision));
    }

    [HttpPost("{itemId}/reopen")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<ReviewDecisionDto>> Reopen(string itemId, [FromBody] ReviewActionRequest? req)
    {
        var mediaRecordId = ResolveMediaRecordId(itemId);
        if (mediaRecordId == null) return NotFound();

        var actorId = User.FindFirst("sub")?.Value ?? User.FindFirst("id")?.Value;
        var decision = await ApplyReviewActionAsync(mediaRecordId, "reopen", req, actorId);
        if (decision == null) return NotFound();

        await _syncService.ApplyTagsForItemAsync(mediaRecordId);
        return Ok(MapDecision(decision));
    }

    [HttpPost("bulk")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<object>> Bulk([FromBody] BulkActionRequest req)
    {
        var validActions = new[] { "approve", "deny", "defer", "reopen" };
        if (!validActions.Contains(req.Action))
            return BadRequest($"Invalid action: {req.Action}");

        var actorId = User.FindFirst("sub")?.Value ?? User.FindFirst("id")?.Value;
        int succeeded = 0, failed = 0;

        foreach (var itemId in req.ItemIds)
        {
            var mediaRecordId = ResolveMediaRecordId(itemId);
            if (mediaRecordId == null) { failed++; continue; }

            var decision = await ApplyReviewActionAsync(mediaRecordId, req.Action, req, actorId);

            if (decision != null)
            {
                await _syncService.ApplyTagsForItemAsync(mediaRecordId);
                succeeded++;
            }
            else failed++;
        }

        return Ok(new { succeeded, failed });
    }

    [HttpGet("{itemId}/history")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<List<DecisionHistoryDto>> History(string itemId)
    {
        var mediaRecordId = ResolveMediaRecordId(itemId);
        if (mediaRecordId == null) return NotFound();

        var history = _reviewService.GetHistory(mediaRecordId);
        return Ok(history.Select(h => new DecisionHistoryDto
        {
            Id = h.Id,
            ViewerProfileId = h.ViewerProfileId,
            PreviousState = h.PreviousState,
            NewState = h.NewState,
            Action = h.Action,
            ActorType = h.ActorType,
            ActorId = h.ActorId,
            DetailsJson = h.DetailsJson,
            CreatedAt = h.CreatedAt
        }).ToList());
    }

    [HttpGet("viewer-profiles")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<List<ViewerProfileDto>> GetViewerProfiles()
    {
        var profiles = LoadViewerProfiles();
        return Ok(profiles.Select(MapProfile).ToList());
    }

    [HttpPost("viewer-profiles")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult<ViewerProfileDto>> CreateViewerProfile([FromBody] CreateViewerProfileRequest req)
    {
        var actorId = User.FindFirst("sub")?.Value ?? User.FindFirst("id")?.Value ?? string.Empty;
        var id = Guid.NewGuid().ToString();
        var tagStem = CreateTagStem(req.DisplayName, req.JellyfinUserId, id);
        var pendingTag = string.IsNullOrWhiteSpace(req.PendingTag) ? $"{tagStem}-pending" : req.PendingTag.Trim();
        var deniedTag = string.IsNullOrWhiteSpace(req.DeniedTag) ? $"{tagStem}-denied" : req.DeniedTag.Trim();
        var allowedTag = string.IsNullOrWhiteSpace(req.AllowedTag) ? $"{tagStem}-allow" : req.AllowedTag.Trim();

        await _db.ExecuteWriteAsync(async conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO viewer_profiles
                    (id, display_name, jellyfin_user_id, age_hint, pending_tag, denied_tag, allowed_tag,
                     created_by_jellyfin_user_id, created_at, updated_at)
                VALUES (@id, @name, @juid, @age, @pending, @denied, @allowed,
                        @creator, datetime('now'), datetime('now'))";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@name", req.DisplayName);
            cmd.Parameters.AddWithValue("@juid", (object?)req.JellyfinUserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@age", (object?)req.AgeHint ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@pending", pendingTag);
            cmd.Parameters.AddWithValue("@denied", deniedTag);
            cmd.Parameters.AddWithValue("@allowed", allowedTag);
            cmd.Parameters.AddWithValue("@creator", actorId);
            cmd.ExecuteNonQuery();
            await Task.CompletedTask;
        });

        var profile = LoadViewerProfile(id);

        if (!string.IsNullOrEmpty(req.JellyfinUserId) && Guid.TryParse(req.JellyfinUserId, out var userGuid))
            await _parentalControlService.ApplyTagPreferencesAsync(userGuid).ConfigureAwait(false);

        return CreatedAtAction(nameof(GetViewerProfiles), MapProfile(profile!));
    }

    [HttpGet("jellyfin-users")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<List<JellyfinUserDto>> GetJellyfinUsers()
    {
        var existingJellyfinIds = LoadViewerProfiles()
            .Where(p => p.JellyfinUserId != null)
            .Select(p => p.JellyfinUserId!)
            .ToHashSet();

        var users = _userManager.Users
            .OrderBy(u => u.Username)
            .Select(u => new JellyfinUserDto
            {
                Id = u.Id.ToString(),
                Name = u.Username,
                HasProfile = existingJellyfinIds.Contains(u.Id.ToString()),
            })
            .ToList();

        return Ok(users);
    }

    // --- Helpers ---

    private string? ResolveMediaRecordId(string itemId)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        // Accept either the internal media_records.id or the jellyfin_item_id
        cmd.CommandText = @"
            SELECT id FROM media_records WHERE id = @id OR jellyfin_item_id = @id LIMIT 1";
        cmd.Parameters.AddWithValue("@id", itemId);
        return cmd.ExecuteScalar()?.ToString();
    }

    private async Task<Models.ReviewDecision?> ApplyReviewActionAsync(
        string mediaRecordId,
        string action,
        ReviewActionRequest? req,
        string? actorId)
    {
        if (!string.IsNullOrEmpty(req?.ViewerProfileId))
        {
            await _reviewService.ApplyViewerActionAsync(
                mediaRecordId, req.ViewerProfileId, action, "user", actorId, req.Notes, req.Reason)
                .ConfigureAwait(false);
            return _reviewService.GetDecision(mediaRecordId) ?? new Models.ReviewDecision
            {
                MediaRecordId = mediaRecordId,
                State = _reviewService.GetViewerDecision(mediaRecordId, req.ViewerProfileId)?.State ?? "pending",
                Source = "manual_review",
            };
        }

        if (req?.AllProfiles == true)
        {
            await _reviewService.ApplyViewerActionToAllProfilesAsync(
                mediaRecordId, action, "user", actorId, req.Notes, req.Reason)
                .ConfigureAwait(false);
            return _reviewService.GetDecision(mediaRecordId) ?? new Models.ReviewDecision
            {
                MediaRecordId = mediaRecordId,
                State = "mixed",
                Source = "manual_review",
            };
        }

        return await _reviewService.ApplyActionAsync(
            mediaRecordId, action, "user", actorId, req?.Notes, req?.Reason)
            .ConfigureAwait(false);
    }

    private static string CreateTagStem(string displayName, string? jellyfinUserId, string profileId)
    {
        var source = string.IsNullOrWhiteSpace(displayName) ? jellyfinUserId ?? profileId : displayName;
        var slug = Regex.Replace(source.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        if (string.IsNullOrWhiteSpace(slug))
            slug = profileId.Replace("-", string.Empty)[..12];
        return $"jelly-review-{slug}";
    }

    private async Task<Models.ReviewDecision?> ApplyReviewActionAsync(
        string mediaRecordId,
        string action,
        BulkActionRequest req,
        string? actorId)
    {
        return await ApplyReviewActionAsync(mediaRecordId, action, new ReviewActionRequest
        {
            Notes = req.Notes,
            Reason = req.Reason,
            ViewerProfileId = req.ViewerProfileId,
            AllProfiles = req.AllProfiles,
        }, actorId).ConfigureAwait(false);
    }

    private List<Models.ViewerProfile> LoadViewerProfiles()
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, display_name, jellyfin_user_id, age_hint, pending_tag, denied_tag, allowed_tag, is_active, created_at
            FROM viewer_profiles WHERE is_active = 1 ORDER BY created_at";
        using var r = cmd.ExecuteReader();
        var list = new List<Models.ViewerProfile>();
        while (r.Read())
        {
            list.Add(new Models.ViewerProfile
            {
                Id = r.GetString(0),
                DisplayName = r.GetString(1),
                JellyfinUserId = r.IsDBNull(2) ? null : r.GetString(2),
                AgeHint = r.IsDBNull(3) ? null : r.GetInt32(3),
                PendingTag = r.IsDBNull(4) ? null : r.GetString(4),
                DeniedTag = r.IsDBNull(5) ? null : r.GetString(5),
                AllowedTag = r.IsDBNull(6) ? null : r.GetString(6),
                IsActive = r.GetInt32(7) == 1,
                CreatedAt = r.GetString(8)
            });
        }
        return list;
    }

    private Models.ViewerProfile? LoadViewerProfile(string id)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, display_name, jellyfin_user_id, age_hint, pending_tag, denied_tag, allowed_tag, is_active, created_at
            FROM viewer_profiles WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new Models.ViewerProfile
        {
            Id = r.GetString(0), DisplayName = r.GetString(1),
            JellyfinUserId = r.IsDBNull(2) ? null : r.GetString(2),
            AgeHint = r.IsDBNull(3) ? null : r.GetInt32(3),
            PendingTag = r.IsDBNull(4) ? null : r.GetString(4),
            DeniedTag = r.IsDBNull(5) ? null : r.GetString(5),
            AllowedTag = r.IsDBNull(6) ? null : r.GetString(6),
            IsActive = r.GetInt32(7) == 1, CreatedAt = r.GetString(8)
        };
    }

    private static ReviewDecisionDto MapDecision(Models.ReviewDecision d) => new()
    {
        Id = d.Id, MediaRecordId = d.MediaRecordId, State = d.State,
        DecisionReason = d.DecisionReason, ReviewerJellyfinUserId = d.ReviewerJellyfinUserId,
        ReviewedAt = d.ReviewedAt, Source = d.Source, Notes = d.Notes
    };

    private static ViewerProfileDto MapProfile(Models.ViewerProfile p) => new()
    {
        Id = p.Id, DisplayName = p.DisplayName, JellyfinUserId = p.JellyfinUserId,
        AgeHint = p.AgeHint, PendingTag = p.PendingTag, DeniedTag = p.DeniedTag,
        AllowedTag = p.AllowedTag, IsActive = p.IsActive, CreatedAt = p.CreatedAt
    };
}
