using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyReview.Data;
using Jellyfin.Plugin.JellyReview.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyReview.Services;

public class ReviewService
{
    private readonly DatabaseManager _db;
    private readonly TagManager _tagManager;
    private readonly ILogger<ReviewService> _logger;

    private static readonly Dictionary<string, string> ActionToState = new()
    {
        ["approve"] = "approved",
        ["deny"] = "denied",
        ["defer"] = "deferred",
        ["reopen"] = "pending",
    };

    public ReviewService(
        DatabaseManager db,
        TagManager tagManager,
        ILogger<ReviewService> logger)
    {
        _db = db;
        _tagManager = tagManager;
        _logger = logger;
    }

    public async Task<ReviewDecision?> ApplyActionAsync(
        string mediaRecordId,
        string action,
        string actorType = "user",
        string? actorId = null,
        string? notes = null,
        string? reason = null,
        string source = "manual_review")
    {
        if (!ActionToState.TryGetValue(action, out var newState))
        {
            _logger.LogWarning("JellyReview: unknown action {Action}", action);
            return null;
        }

        return await _db.ExecuteWriteAsync(async conn =>
        {
            var decision = GetOrCreateDecision(conn, mediaRecordId);
            var previousState = decision.State;

            decision.State = newState;
            decision.ReviewedAt = DateTime.UtcNow.ToString("O");
            decision.ReviewerJellyfinUserId = actorType == "user" ? actorId : null;
            decision.Notes = notes;
            decision.DecisionReason = reason;
            decision.Source = source;
            decision.NeedsResync = false;
            decision.UpdatedAt = DateTime.UtcNow.ToString("O");

            UpdateDecision(conn, decision);

            InsertHistory(conn, new DecisionHistory
            {
                Id = Guid.NewGuid().ToString(),
                MediaRecordId = mediaRecordId,
                PreviousState = previousState,
                NewState = newState,
                Action = action,
                ActorType = actorType,
                ActorId = actorId,
                DetailsJson = notes != null || reason != null
                    ? JsonSerializer.Serialize(new { notes, reason })
                    : null,
                CreatedAt = DateTime.UtcNow.ToString("O")
            });

            await Task.CompletedTask;
            return decision;
        });
    }

    public async Task ApplyTagsForDecisionAsync(string mediaRecordId, string jellyfinItemId)
    {
        using var conn = _db.CreateConnection();
        var decision = GetDecision(conn, mediaRecordId);
        if (decision == null) return;

        if (Guid.TryParse(jellyfinItemId, out var itemGuid))
            await _tagManager.ApplyDecisionTagsAsync(itemGuid, decision.State).ConfigureAwait(false);
    }

    public async Task<ViewerDecision?> ApplyViewerActionAsync(
        string mediaRecordId,
        string viewerProfileId,
        string action,
        string actorType = "user",
        string? actorId = null,
        string? notes = null,
        string? reason = null,
        string source = "manual_review")
    {
        if (!ActionToState.TryGetValue(action, out var newState))
            return null;

        return await _db.ExecuteWriteAsync(async conn =>
        {
            var decision = GetOrCreateViewerDecision(conn, mediaRecordId, viewerProfileId);
            var previousState = decision.State;

            decision.State = newState;
            decision.ReviewedAt = DateTime.UtcNow.ToString("O");
            decision.ReviewerJellyfinUserId = actorType == "user" ? actorId : null;
            decision.Notes = notes;
            decision.DecisionReason = reason;
            decision.Source = source;
            decision.UpdatedAt = DateTime.UtcNow.ToString("O");

            UpsertViewerDecision(conn, decision);

            InsertHistory(conn, new DecisionHistory
            {
                Id = Guid.NewGuid().ToString(),
                MediaRecordId = mediaRecordId,
                ViewerProfileId = viewerProfileId,
                PreviousState = previousState,
                NewState = newState,
                Action = action,
                ActorType = actorType,
                ActorId = actorId,
                DetailsJson = notes != null || reason != null
                    ? JsonSerializer.Serialize(new { notes, reason })
                    : null,
                CreatedAt = DateTime.UtcNow.ToString("O")
            });

            await Task.CompletedTask;
            return decision;
        });
    }

    public async Task<List<ViewerDecision>> ApplyViewerActionToAllProfilesAsync(
        string mediaRecordId,
        string action,
        string actorType = "user",
        string? actorId = null,
        string? notes = null,
        string? reason = null,
        string source = "manual_review")
    {
        var results = new List<ViewerDecision>();
        foreach (var profileId in GetActiveViewerProfileIds())
        {
            var decision = await ApplyViewerActionAsync(
                mediaRecordId, profileId, action, actorType, actorId, notes, reason, source)
                .ConfigureAwait(false);
            if (decision != null)
                results.Add(decision);
        }

        return results;
    }

    public List<DecisionHistory> GetHistory(string mediaRecordId, int limit = 50)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, media_record_id, viewer_profile_id, previous_state, new_state,
                   action, actor_type, actor_id, details_json, created_at
            FROM decision_history
            WHERE media_record_id = @id
            ORDER BY created_at DESC
            LIMIT @limit";
        cmd.Parameters.AddWithValue("@id", mediaRecordId);
        cmd.Parameters.AddWithValue("@limit", limit);

        var result = new List<DecisionHistory>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new DecisionHistory
            {
                Id = reader.GetString(0),
                MediaRecordId = reader.GetString(1),
                ViewerProfileId = reader.IsDBNull(2) ? null : reader.GetString(2),
                PreviousState = reader.IsDBNull(3) ? null : reader.GetString(3),
                NewState = reader.GetString(4),
                Action = reader.GetString(5),
                ActorType = reader.GetString(6),
                ActorId = reader.IsDBNull(7) ? null : reader.GetString(7),
                DetailsJson = reader.IsDBNull(8) ? null : reader.GetString(8),
                CreatedAt = reader.GetString(9)
            });
        }
        return result;
    }

    public ReviewDecision? GetDecision(string mediaRecordId)
    {
        using var conn = _db.CreateConnection();
        return GetDecision(conn, mediaRecordId);
    }

    public ViewerDecision? GetViewerDecision(string mediaRecordId, string viewerProfileId)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, viewer_profile_id, media_record_id, state, decision_reason,
                   reviewer_jellyfin_user_id, reviewed_at, source, notes, needs_resync,
                   created_at, updated_at
            FROM viewer_decisions
            WHERE media_record_id = @mid AND viewer_profile_id = @vpid";
        cmd.Parameters.AddWithValue("@mid", mediaRecordId);
        cmd.Parameters.AddWithValue("@vpid", viewerProfileId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return ReadViewerDecision(reader);
    }

    public List<string> GetActiveViewerProfileIds()
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM viewer_profiles WHERE is_active = 1 ORDER BY created_at";
        using var reader = cmd.ExecuteReader();
        var result = new List<string>();
        while (reader.Read())
            result.Add(reader.GetString(0));
        return result;
    }

    private static ReviewDecision? GetDecision(SqliteConnection conn, string mediaRecordId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, media_record_id, state, decision_reason, reviewer_jellyfin_user_id,
                   reviewed_at, source, notes, needs_resync, created_at, updated_at
            FROM review_decisions WHERE media_record_id = @id";
        cmd.Parameters.AddWithValue("@id", mediaRecordId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return ReadDecision(reader);
    }

    private static ReviewDecision GetOrCreateDecision(SqliteConnection conn, string mediaRecordId)
    {
        var existing = GetDecision(conn, mediaRecordId);
        if (existing != null) return existing;

        var decision = new ReviewDecision
        {
            Id = Guid.NewGuid().ToString(),
            MediaRecordId = mediaRecordId,
            State = "pending",
            Source = "sync_reconciliation",
            CreatedAt = DateTime.UtcNow.ToString("O"),
            UpdatedAt = DateTime.UtcNow.ToString("O")
        };

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO review_decisions
                (id, media_record_id, state, source, created_at, updated_at)
            VALUES (@id, @mid, @state, @source, @created, @updated)";
        cmd.Parameters.AddWithValue("@id", decision.Id);
        cmd.Parameters.AddWithValue("@mid", decision.MediaRecordId);
        cmd.Parameters.AddWithValue("@state", decision.State);
        cmd.Parameters.AddWithValue("@source", decision.Source);
        cmd.Parameters.AddWithValue("@created", decision.CreatedAt);
        cmd.Parameters.AddWithValue("@updated", decision.UpdatedAt);
        cmd.ExecuteNonQuery();

        return decision;
    }

    private static void UpdateDecision(SqliteConnection conn, ReviewDecision d)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE review_decisions SET
                state = @state,
                decision_reason = @reason,
                reviewer_jellyfin_user_id = @reviewer,
                reviewed_at = @reviewedAt,
                source = @source,
                notes = @notes,
                needs_resync = @needs_resync,
                updated_at = @updated
            WHERE id = @id";
        cmd.Parameters.AddWithValue("@state", d.State);
        cmd.Parameters.AddWithValue("@reason", (object?)d.DecisionReason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@reviewer", (object?)d.ReviewerJellyfinUserId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@reviewedAt", (object?)d.ReviewedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@source", d.Source);
        cmd.Parameters.AddWithValue("@notes", (object?)d.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@needs_resync", d.NeedsResync ? 1 : 0);
        cmd.Parameters.AddWithValue("@updated", d.UpdatedAt);
        cmd.Parameters.AddWithValue("@id", d.Id);
        cmd.ExecuteNonQuery();
    }

    private static ReviewDecision ReadDecision(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        MediaRecordId = r.GetString(1),
        State = r.GetString(2),
        DecisionReason = r.IsDBNull(3) ? null : r.GetString(3),
        ReviewerJellyfinUserId = r.IsDBNull(4) ? null : r.GetString(4),
        ReviewedAt = r.IsDBNull(5) ? null : r.GetString(5),
        Source = r.GetString(6),
        Notes = r.IsDBNull(7) ? null : r.GetString(7),
        NeedsResync = r.GetInt32(8) == 1,
        CreatedAt = r.GetString(9),
        UpdatedAt = r.GetString(10)
    };

    private static ViewerDecision GetOrCreateViewerDecision(
        SqliteConnection conn, string mediaRecordId, string viewerProfileId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, viewer_profile_id, media_record_id, state, decision_reason,
                   reviewer_jellyfin_user_id, reviewed_at, source, notes, needs_resync,
                   created_at, updated_at
            FROM viewer_decisions
            WHERE media_record_id = @mid AND viewer_profile_id = @vpid";
        cmd.Parameters.AddWithValue("@mid", mediaRecordId);
        cmd.Parameters.AddWithValue("@vpid", viewerProfileId);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return ReadViewerDecision(reader);
        }

        return new ViewerDecision
        {
            Id = Guid.NewGuid().ToString(),
            ViewerProfileId = viewerProfileId,
            MediaRecordId = mediaRecordId,
            State = "pending",
            Source = "manual_review",
            CreatedAt = DateTime.UtcNow.ToString("O"),
            UpdatedAt = DateTime.UtcNow.ToString("O")
        };
    }

    private static ViewerDecision ReadViewerDecision(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        ViewerProfileId = reader.GetString(1),
        MediaRecordId = reader.GetString(2),
        State = reader.GetString(3),
        DecisionReason = reader.IsDBNull(4) ? null : reader.GetString(4),
        ReviewerJellyfinUserId = reader.IsDBNull(5) ? null : reader.GetString(5),
        ReviewedAt = reader.IsDBNull(6) ? null : reader.GetString(6),
        Source = reader.GetString(7),
        Notes = reader.IsDBNull(8) ? null : reader.GetString(8),
        NeedsResync = reader.GetInt32(9) == 1,
        CreatedAt = reader.GetString(10),
        UpdatedAt = reader.GetString(11)
    };

    private static void UpsertViewerDecision(SqliteConnection conn, ViewerDecision d)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO viewer_decisions
                (id, viewer_profile_id, media_record_id, state, decision_reason,
                 reviewer_jellyfin_user_id, reviewed_at, source, notes, needs_resync,
                 created_at, updated_at)
            VALUES
                (@id, @vpid, @mid, @state, @reason, @reviewer, @reviewedAt,
                 @source, @notes, @needs_resync, @created, @updated)
            ON CONFLICT(viewer_profile_id, media_record_id) DO UPDATE SET
                state = excluded.state,
                decision_reason = excluded.decision_reason,
                reviewer_jellyfin_user_id = excluded.reviewer_jellyfin_user_id,
                reviewed_at = excluded.reviewed_at,
                source = excluded.source,
                notes = excluded.notes,
                needs_resync = excluded.needs_resync,
                updated_at = excluded.updated_at";
        cmd.Parameters.AddWithValue("@id", d.Id);
        cmd.Parameters.AddWithValue("@vpid", d.ViewerProfileId);
        cmd.Parameters.AddWithValue("@mid", d.MediaRecordId);
        cmd.Parameters.AddWithValue("@state", d.State);
        cmd.Parameters.AddWithValue("@reason", (object?)d.DecisionReason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@reviewer", (object?)d.ReviewerJellyfinUserId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@reviewedAt", (object?)d.ReviewedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@source", d.Source);
        cmd.Parameters.AddWithValue("@notes", (object?)d.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@needs_resync", d.NeedsResync ? 1 : 0);
        cmd.Parameters.AddWithValue("@created", d.CreatedAt);
        cmd.Parameters.AddWithValue("@updated", d.UpdatedAt);
        cmd.ExecuteNonQuery();
    }

    private static void InsertHistory(SqliteConnection conn, DecisionHistory h)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO decision_history
                (id, media_record_id, viewer_profile_id, previous_state, new_state,
                 action, actor_type, actor_id, details_json, created_at)
            VALUES
                (@id, @mid, @vpid, @prev, @new, @action, @actorType,
                 @actorId, @details, @created)";
        cmd.Parameters.AddWithValue("@id", h.Id);
        cmd.Parameters.AddWithValue("@mid", h.MediaRecordId);
        cmd.Parameters.AddWithValue("@vpid", (object?)h.ViewerProfileId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@prev", (object?)h.PreviousState ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@new", h.NewState);
        cmd.Parameters.AddWithValue("@action", h.Action);
        cmd.Parameters.AddWithValue("@actorType", h.ActorType);
        cmd.Parameters.AddWithValue("@actorId", (object?)h.ActorId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@details", (object?)h.DetailsJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@created", h.CreatedAt);
        cmd.ExecuteNonQuery();
    }
}
