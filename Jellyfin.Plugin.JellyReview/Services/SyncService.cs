using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.JellyReview.Configuration;
using Jellyfin.Plugin.JellyReview.Data;
using Jellyfin.Plugin.JellyReview.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyReview.Services;

public class SyncService
{
    private readonly DatabaseManager _db;
    private readonly ILibraryManager _libraryManager;
    private readonly RuleEngine _ruleEngine;
    private readonly TagManager _tagManager;
    private readonly NotificationService _notifications;
    private readonly ILogger<SyncService> _logger;

    private static readonly HashSet<string> AutoReapplySources = new()
        { "auto_rule", "sync_reconciliation", "migration" };

    public SyncService(
        DatabaseManager db,
        ILibraryManager libraryManager,
        RuleEngine ruleEngine,
        TagManager tagManager,
        NotificationService notifications,
        ILogger<SyncService> logger)
    {
        _db = db;
        _libraryManager = libraryManager;
        _ruleEngine = ruleEngine;
        _tagManager = tagManager;
        _notifications = notifications;
        _logger = logger;
    }

    private PluginConfiguration Config => Plugin.Instance.Configuration;

    /// Called by LibraryEventListener when a new item is added.
    public async Task HandleNewItemAsync(BaseItem item)
    {
        var record = await UpsertMediaRecordAsync(item, isNew: true).ConfigureAwait(false);
        if (record == null) return;

        using var conn = _db.CreateConnection();
        var existingDecision = GetDecision(conn, record.Id);
        var profiles = LoadActiveProfiles(conn);

        if (profiles.Count > 0)
        {
            await EnsureViewerDecisionsAsync(item, record, profiles, existingDecision).ConfigureAwait(false);
            await ApplyTagsForItemAsync(record.Id).ConfigureAwait(false);
            return;
        }

        if (existingDecision != null)
        {
            await ReapplyRulesIfEligibleAsync(item, record, existingDecision).ConfigureAwait(false);
            return;
        }

        // Determine initial state from existing tags
        var tags = item.Tags ?? Array.Empty<string>();
        bool hasPending = tags.Contains(Config.PendingTag);
        bool hasDenied = tags.Contains(Config.DeniedTag);
        bool hasAllowed = tags.Contains(Config.AllowedTag);

        string? initialState = null;
        string source = "sync_reconciliation";

        if (hasDenied)
            initialState = "denied";
        else if (hasPending)
            initialState = "pending";
        else if (hasAllowed)
            initialState = "approved";
        else if (Config.AutoRulesEnabled)
        {
            var (action, matchedRule) = await EvaluateAutoRulesAsync(record).ConfigureAwait(false);
            initialState = RuleActionToState(action);
            source = "auto_rule";

            if (initialState is "approved" or "denied")
            {
                await _tagManager.ApplyDecisionTagsAsync(item.Id, initialState).ConfigureAwait(false);
            }
            else
            {
                await _tagManager.ApplyPendingTagAsync(item.Id).ConfigureAwait(false);
            }
        }
        else
        {
            initialState = "pending";
            await _tagManager.ApplyPendingTagAsync(item.Id).ConfigureAwait(false);
        }

        await _db.ExecuteWriteAsync(async writeConn =>
        {
            InsertDecision(writeConn, record.Id, initialState!, source);
            InsertHistory(writeConn, record.Id, null, initialState!, "sync_import", "system");
            await Task.CompletedTask;
        }).ConfigureAwait(false);

        if (initialState == "pending")
            await _notifications.NotifyPendingReviewAsync(record.Id).ConfigureAwait(false);
    }

    private async Task EnsureViewerDecisionsAsync(
        BaseItem item,
        MediaRecord record,
        IReadOnlyCollection<ViewerProfile> profiles,
        ReviewDecision? legacyDecision)
    {
        foreach (var profile in profiles)
        {
            var existing = GetViewerDecision(record.Id, profile.Id);
            if (existing != null)
            {
                await ReapplyViewerRulesIfEligibleAsync(record, existing).ConfigureAwait(false);
                continue;
            }

            var (state, source) = await DetermineInitialViewerStateAsync(item, record, profile.Id, legacyDecision)
                .ConfigureAwait(false);

            await _db.ExecuteWriteAsync(async writeConn =>
            {
                InsertViewerDecision(writeConn, record.Id, profile.Id, state, source);
                InsertHistory(writeConn, record.Id, null, state, "sync_import", "system", profile.Id);
                await Task.CompletedTask;
            }).ConfigureAwait(false);

            if (state == "pending")
                await _notifications.NotifyPendingReviewAsync(record.Id, profile.Id).ConfigureAwait(false);
        }
    }

    private async Task<(string State, string Source)> DetermineInitialViewerStateAsync(
        BaseItem item,
        MediaRecord record,
        string viewerProfileId,
        ReviewDecision? legacyDecision)
    {
        var tags = item.Tags ?? Array.Empty<string>();
        if (legacyDecision != null)
            return (legacyDecision.State, "migration");

        if (tags.Contains(Config.DeniedTag))
            return ("denied", "sync_reconciliation");
        if (tags.Contains(Config.PendingTag))
            return ("pending", "sync_reconciliation");
        if (tags.Contains(Config.AllowedTag))
            return ("approved", "sync_reconciliation");

        if (!Config.AutoRulesEnabled)
            return ("pending", "sync_reconciliation");

        await _ruleEngine.SeedStarterRulesAsync().ConfigureAwait(false);
        var (action, _) = await _ruleEngine.EvaluateAsync(record, viewerProfileId).ConfigureAwait(false);
        return (RuleActionToState(action), "auto_rule");
    }

    private async Task ReapplyViewerRulesIfEligibleAsync(MediaRecord record, ViewerDecision existingDecision)
    {
        if (!Config.AutoRulesEnabled) return;
        if (!AutoReapplySources.Contains(existingDecision.Source)) return;

        await _ruleEngine.SeedStarterRulesAsync().ConfigureAwait(false);
        var (action, _) = await _ruleEngine.EvaluateAsync(record, existingDecision.ViewerProfileId).ConfigureAwait(false);
        var newState = RuleActionToState(action);
        if (newState == existingDecision.State) return;

        await _db.ExecuteWriteAsync(async writeConn =>
        {
            UpdateViewerDecision(writeConn, existingDecision.Id, newState, "auto_rule");
            InsertHistory(writeConn, record.Id, existingDecision.State, newState, "sync_reapply_rules", "system", existingDecision.ViewerProfileId);
            await Task.CompletedTask;
        }).ConfigureAwait(false);

        if (newState == "pending")
            await _notifications.NotifyPendingReviewAsync(record.Id, existingDecision.ViewerProfileId).ConfigureAwait(false);
    }

    private async Task ReapplyRulesIfEligibleAsync(BaseItem item, MediaRecord record, ReviewDecision existingDecision)
    {
        if (!Config.AutoRulesEnabled) return;
        if (!AutoReapplySources.Contains(existingDecision.Source)) return;

        var (action, _) = await EvaluateAutoRulesAsync(record).ConfigureAwait(false);
        var newState = RuleActionToState(action);
        var source = "auto_rule";

        if (newState == existingDecision.State) return;

        await _db.ExecuteWriteAsync(async writeConn =>
        {
            UpdateDecision(writeConn, existingDecision.Id, newState, source);
            InsertHistory(writeConn, record.Id, existingDecision.State, newState, "sync_reapply_rules", "system");
            await Task.CompletedTask;
        }).ConfigureAwait(false);

        if (newState is "approved" or "denied")
        {
            await _tagManager.ApplyDecisionTagsAsync(item.Id, newState).ConfigureAwait(false);
        }
        else
        {
            await _tagManager.ApplyPendingTagAsync(item.Id).ConfigureAwait(false);
            await _notifications.NotifyPendingReviewAsync(record.Id).ConfigureAwait(false);
        }
    }

    private async Task<(string? Action, ReviewRule? MatchedRule)> EvaluateAutoRulesAsync(MediaRecord record)
    {
        await _ruleEngine.SeedStarterRulesAsync().ConfigureAwait(false);
        return await _ruleEngine.EvaluateAsync(record).ConfigureAwait(false);
    }

    /// Looks up the Jellyfin item ID for a media record and applies decision tags.
    public async Task ApplyTagsForItemAsync(string mediaRecordId)
    {
        string? jellyfinItemId;
        using (var conn = _db.CreateConnection())
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT jellyfin_item_id FROM media_records WHERE id = @id LIMIT 1";
            cmd.Parameters.AddWithValue("@id", mediaRecordId);
            jellyfinItemId = cmd.ExecuteScalar()?.ToString();
        }

        if (string.IsNullOrEmpty(jellyfinItemId)) return;
        var profileStates = LoadViewerProfileTagStates(mediaRecordId);
        if (profileStates.Count > 0)
        {
            if (Guid.TryParse(jellyfinItemId, out var viewerItemGuid))
                await _tagManager.ApplyViewerDecisionTagsAsync(viewerItemGuid, profileStates).ConfigureAwait(false);
            return;
        }

        string? state;
        using (var conn = _db.CreateConnection())
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT state FROM review_decisions WHERE media_record_id = @id LIMIT 1";
            cmd.Parameters.AddWithValue("@id", mediaRecordId);
            state = cmd.ExecuteScalar()?.ToString();
        }

        if (string.IsNullOrEmpty(state)) return;
        if (Guid.TryParse(jellyfinItemId, out var itemGuid))
            await _tagManager.ApplyDecisionTagsAsync(itemGuid, state).ConfigureAwait(false);
    }

    public async Task ApplyTagsForAllDecisionRecordsAsync()
    {
        var mediaRecordIds = new List<string>();
        using (var conn = _db.CreateConnection())
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT DISTINCT media_record_id FROM viewer_decisions
                UNION
                SELECT DISTINCT media_record_id FROM review_decisions";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                mediaRecordIds.Add(reader.GetString(0));
        }

        foreach (var mediaRecordId in mediaRecordIds)
            await ApplyTagsForItemAsync(mediaRecordId).ConfigureAwait(false);
    }

    public async Task<SyncResult> RunIncrementalSyncAsync(CancellationToken cancellationToken = default)
    {
        var cursor = GetOrCreateCursor("jellyfin_recent_items");
        var since = cursor.LastSuccessAt != null
            ? DateTime.Parse(cursor.LastSuccessAt, null, System.Globalization.DateTimeStyles.RoundtripKind)
            : DateTime.UtcNow.AddDays(-7);

        var items = _libraryManager
            .GetItemList(CreateMovieSeriesQuery())
            .Where(item => item.DateCreated >= since);
        int imported = 0, errors = 0;

        foreach (var item in items)
        {
            if (cancellationToken.IsCancellationRequested) break;
            try
            {
                var isNew = !MediaRecordExists(item.Id.ToString());
                if (isNew)
                {
                    await HandleNewItemAsync(item).ConfigureAwait(false);
                    imported++;
                }
                else
                {
                    var record = await UpsertMediaRecordAsync(item, isNew: false).ConfigureAwait(false);
                    if (record != null)
                    {
                        using var conn = _db.CreateConnection();
                        var existingDecision = GetDecision(conn, record.Id);
                        var profiles = LoadActiveProfiles(conn);
                        if (profiles.Count > 0)
                        {
                            await EnsureViewerDecisionsAsync(item, record, profiles, existingDecision).ConfigureAwait(false);
                            await ApplyTagsForItemAsync(record.Id).ConfigureAwait(false);
                        }
                        else if (existingDecision != null)
                            await ReapplyRulesIfEligibleAsync(item, record, existingDecision).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "JellyReview: incremental sync failed for {ItemId}", item.Id);
                errors++;
            }
        }

        await UpdateCursorSuccessAsync("jellyfin_recent_items").ConfigureAwait(false);
        return new SyncResult { Imported = imported, Errors = errors };
    }

    public async Task<SyncResult> RunFullImportAsync(
        List<string>? libraryIds = null, CancellationToken cancellationToken = default)
    {
        IEnumerable<BaseItem> allItems = _libraryManager.GetItemList(CreateMovieSeriesQuery());

        // Filter by selected libraries if configured
        var selectedIds = libraryIds
            ?? (string.IsNullOrEmpty(Config.SelectedLibraryIds)
                ? null
                : JsonSerializer.Deserialize<List<string>>(Config.SelectedLibraryIds));

        if (selectedIds?.Count > 0)
        {
            var selectedSet = selectedIds.Select(id => Guid.TryParse(id, out var g) ? g : Guid.Empty).ToHashSet();
            allItems = allItems.Where(item =>
            {
                var parents = GetParentChain(item);
                return parents.Any(p => selectedSet.Contains(p.Id));
            });
        }

        int imported = 0, updated = 0, errors = 0;
        var seenJellyfinIds = new HashSet<string>();

        foreach (var item in allItems)
        {
            if (cancellationToken.IsCancellationRequested) break;
            seenJellyfinIds.Add(item.Id.ToString());

            try
            {
                var isNew = !MediaRecordExists(item.Id.ToString());
                await HandleNewItemAsync(item).ConfigureAwait(false);
                if (isNew) imported++;
                else updated++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "JellyReview: full import failed for {ItemId}", item.Id);
                errors++;
            }
        }

        var deleted = MarkMissingDeleted(seenJellyfinIds);

        await UpdateCursorSuccessAsync("jellyfin_recent_items").ConfigureAwait(false);
        return new SyncResult { Imported = imported, Updated = updated, Deleted = deleted, Errors = errors };
    }

    private static InternalItemsQuery CreateMovieSeriesQuery() => new()
    {
        IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
        Recursive = true,
    };

    private async Task<MediaRecord?> UpsertMediaRecordAsync(BaseItem item, bool isNew)
    {
        var jellyfinId = item.Id.ToString();
        var runtimeMinutes = item.RunTimeTicks.HasValue
            ? (int)(item.RunTimeTicks.Value / TimeSpan.TicksPerMinute)
            : (int?)null;
        var genresJson = JsonSerializer.Serialize(item.Genres ?? Array.Empty<string>());
        var tagsJson = JsonSerializer.Serialize(item.Tags ?? Array.Empty<string>());

        return await _db.ExecuteWriteAsync(async conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO media_records
                    (id, jellyfin_item_id, media_type, title, sort_title, year,
                     official_rating, community_rating, runtime_minutes, overview,
                     genres_json, tags_snapshot_json, status, last_seen_at, last_synced_at)
                VALUES
                    (@id, @jid, @type, @title, @sort, @year,
                     @rating, @community, @runtime, @overview,
                     @genres, @tags, 'active', datetime('now'), datetime('now'))
                ON CONFLICT(jellyfin_item_id) DO UPDATE SET
                    title = excluded.title,
                    sort_title = excluded.sort_title,
                    year = excluded.year,
                    official_rating = excluded.official_rating,
                    community_rating = excluded.community_rating,
                    runtime_minutes = excluded.runtime_minutes,
                    overview = excluded.overview,
                    genres_json = excluded.genres_json,
                    tags_snapshot_json = excluded.tags_snapshot_json,
                    status = 'active',
                    last_seen_at = datetime('now'),
                    last_synced_at = datetime('now')
                RETURNING id, jellyfin_item_id, media_type, title, sort_title, year,
                    official_rating, community_rating, runtime_minutes, overview,
                    genres_json, tags_snapshot_json, status, first_seen_at, last_seen_at, last_synced_at";

            cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("@jid", jellyfinId);
            cmd.Parameters.AddWithValue("@type", item is Movie ? "movie" : "series");
            cmd.Parameters.AddWithValue("@title", item.Name ?? string.Empty);
            cmd.Parameters.AddWithValue("@sort", (object?)item.SortName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@year", (object?)item.ProductionYear ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@rating", (object?)item.OfficialRating ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@community", (object?)item.CommunityRating ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@runtime", (object?)runtimeMinutes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@overview", (object?)item.Overview ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@genres", genresJson);
            cmd.Parameters.AddWithValue("@tags", tagsJson);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;

            await Task.CompletedTask;
            return new MediaRecord
            {
                Id = reader.GetString(0),
                JellyfinItemId = reader.GetString(1),
                MediaType = reader.GetString(2),
                Title = reader.GetString(3),
                SortTitle = reader.IsDBNull(4) ? null : reader.GetString(4),
                Year = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                OfficialRating = reader.IsDBNull(6) ? null : reader.GetString(6),
                CommunityRating = reader.IsDBNull(7) ? null : reader.GetDouble(7),
                RuntimeMinutes = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                Overview = reader.IsDBNull(9) ? null : reader.GetString(9),
                GenresJson = reader.IsDBNull(10) ? null : reader.GetString(10),
                TagsSnapshotJson = reader.IsDBNull(11) ? null : reader.GetString(11),
                Status = reader.GetString(12),
                FirstSeenAt = reader.GetString(13),
                LastSeenAt = reader.GetString(14),
                LastSyncedAt = reader.IsDBNull(15) ? null : reader.GetString(15),
            };
        }).ConfigureAwait(false);
    }

    private bool MediaRecordExists(string jellyfinItemId)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM media_records WHERE jellyfin_item_id = @jid LIMIT 1";
        cmd.Parameters.AddWithValue("@jid", jellyfinItemId);
        return cmd.ExecuteScalar() != null;
    }

    private int MarkMissingDeleted(HashSet<string> seenJellyfinIds)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, jellyfin_item_id FROM media_records
            WHERE status = 'active' AND media_type IN ('movie','series')";

        var toDelete = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var jid = reader.GetString(1);
            if (!seenJellyfinIds.Contains(jid))
                toDelete.Add(reader.GetString(0));
        }

        if (toDelete.Count == 0) return 0;

        // Need write lock — reuse conn isn't thread-safe for reads+writes, open new one
        _db.WriteLock.Wait();
        try
        {
            using var writeConn = _db.CreateConnection();
            foreach (var id in toDelete)
            {
                using var updateCmd = writeConn.CreateCommand();
                updateCmd.CommandText = "UPDATE media_records SET status='deleted' WHERE id=@id";
                updateCmd.Parameters.AddWithValue("@id", id);
                updateCmd.ExecuteNonQuery();
            }
        }
        finally
        {
            _db.WriteLock.Release();
        }

        return toDelete.Count;
    }

    private SyncCursor GetOrCreateCursor(string source)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, source, cursor_value, last_run_at, last_success_at, error_count FROM sync_cursors WHERE source = @s";
        cmd.Parameters.AddWithValue("@s", source);
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return new SyncCursor
            {
                Id = reader.GetString(0),
                Source = reader.GetString(1),
                CursorValue = reader.IsDBNull(2) ? null : reader.GetString(2),
                LastRunAt = reader.IsDBNull(3) ? null : reader.GetString(3),
                LastSuccessAt = reader.IsDBNull(4) ? null : reader.GetString(4),
                ErrorCount = reader.GetInt32(5)
            };
        }

        var cursor = new SyncCursor { Id = Guid.NewGuid().ToString(), Source = source };
        reader.Close();

        _db.WriteLock.Wait();
        try
        {
            using var writeConn = _db.CreateConnection();
            using var insertCmd = writeConn.CreateCommand();
            insertCmd.CommandText = "INSERT OR IGNORE INTO sync_cursors (id, source) VALUES (@id, @s)";
            insertCmd.Parameters.AddWithValue("@id", cursor.Id);
            insertCmd.Parameters.AddWithValue("@s", source);
            insertCmd.ExecuteNonQuery();
        }
        finally
        {
            _db.WriteLock.Release();
        }

        return cursor;
    }

    private async Task UpdateCursorSuccessAsync(string source)
    {
        await _db.ExecuteWriteAsync(async conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE sync_cursors
                SET last_run_at = datetime('now'), last_success_at = datetime('now'), error_count = 0
                WHERE source = @s";
            cmd.Parameters.AddWithValue("@s", source);
            cmd.ExecuteNonQuery();
            await Task.CompletedTask;
        }).ConfigureAwait(false);
    }

    private static string RuleActionToState(string? action) => action switch
    {
        "auto_approve" => "approved",
        "auto_deny" => "denied",
        "defer" => "deferred",
        _ => "pending"
    };

    private static void InsertDecision(SqliteConnection conn, string mediaRecordId, string state, string source)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT OR IGNORE INTO review_decisions
                (id, media_record_id, state, source, created_at, updated_at)
            VALUES (@id, @mid, @state, @source, datetime('now'), datetime('now'))";
        cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
        cmd.Parameters.AddWithValue("@mid", mediaRecordId);
        cmd.Parameters.AddWithValue("@state", state);
        cmd.Parameters.AddWithValue("@source", source);
        cmd.ExecuteNonQuery();
    }

    private static void InsertHistory(
        SqliteConnection conn, string mediaRecordId, string? previous, string newState,
        string action, string actorType, string? viewerProfileId = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO decision_history
                (id, media_record_id, viewer_profile_id, previous_state, new_state, action, actor_type, created_at)
            VALUES (@id, @mid, @vpid, @prev, @new, @action, @actor, datetime('now'))";
        cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
        cmd.Parameters.AddWithValue("@mid", mediaRecordId);
        cmd.Parameters.AddWithValue("@vpid", (object?)viewerProfileId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@prev", (object?)previous ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@new", newState);
        cmd.Parameters.AddWithValue("@action", action);
        cmd.Parameters.AddWithValue("@actor", actorType);
        cmd.ExecuteNonQuery();
    }

    private static void InsertViewerDecision(
        SqliteConnection conn, string mediaRecordId, string viewerProfileId, string state, string source)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT OR IGNORE INTO viewer_decisions
                (id, viewer_profile_id, media_record_id, state, source, created_at, updated_at)
            VALUES (@id, @vpid, @mid, @state, @source, datetime('now'), datetime('now'))";
        cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
        cmd.Parameters.AddWithValue("@vpid", viewerProfileId);
        cmd.Parameters.AddWithValue("@mid", mediaRecordId);
        cmd.Parameters.AddWithValue("@state", state);
        cmd.Parameters.AddWithValue("@source", source);
        cmd.ExecuteNonQuery();
    }

    private static void UpdateViewerDecision(SqliteConnection conn, string decisionId, string state, string source)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE viewer_decisions
            SET state = @state,
                source = @source,
                updated_at = datetime('now')
            WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", decisionId);
        cmd.Parameters.AddWithValue("@state", state);
        cmd.Parameters.AddWithValue("@source", source);
        cmd.ExecuteNonQuery();
    }

    private static void UpdateDecision(SqliteConnection conn, string decisionId, string state, string source)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE review_decisions
            SET state = @state,
                source = @source,
                updated_at = datetime('now')
            WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", decisionId);
        cmd.Parameters.AddWithValue("@state", state);
        cmd.Parameters.AddWithValue("@source", source);
        cmd.ExecuteNonQuery();
    }

    private static ReviewDecision? GetDecision(SqliteConnection conn, string mediaRecordId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, state, source FROM review_decisions WHERE media_record_id = @id LIMIT 1";
        cmd.Parameters.AddWithValue("@id", mediaRecordId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        return new ReviewDecision
        {
            Id = reader.GetString(0),
            MediaRecordId = mediaRecordId,
            State = reader.GetString(1),
            Source = reader.GetString(2),
        };
    }

    private ViewerDecision? GetViewerDecision(string mediaRecordId, string viewerProfileId)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, viewer_profile_id, media_record_id, state, source
            FROM viewer_decisions
            WHERE media_record_id = @mid AND viewer_profile_id = @vpid
            LIMIT 1";
        cmd.Parameters.AddWithValue("@mid", mediaRecordId);
        cmd.Parameters.AddWithValue("@vpid", viewerProfileId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        return new ViewerDecision
        {
            Id = reader.GetString(0),
            ViewerProfileId = reader.GetString(1),
            MediaRecordId = reader.GetString(2),
            State = reader.GetString(3),
            Source = reader.GetString(4),
        };
    }

    private static List<ViewerProfile> LoadActiveProfiles(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, display_name, jellyfin_user_id, age_hint, pending_tag, denied_tag, allowed_tag
            FROM viewer_profiles
            WHERE is_active = 1
            ORDER BY created_at";
        using var reader = cmd.ExecuteReader();
        var profiles = new List<ViewerProfile>();
        while (reader.Read())
        {
            profiles.Add(new ViewerProfile
            {
                Id = reader.GetString(0),
                DisplayName = reader.GetString(1),
                JellyfinUserId = reader.IsDBNull(2) ? null : reader.GetString(2),
                AgeHint = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                PendingTag = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                DeniedTag = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                AllowedTag = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
            });
        }

        return profiles;
    }

    private List<ViewerProfileTagState> LoadViewerProfileTagStates(string mediaRecordId)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT vp.id,
                   COALESCE(vd.state, 'pending') as state,
                   vp.pending_tag,
                   vp.denied_tag,
                   vp.allowed_tag
            FROM viewer_profiles vp
            LEFT JOIN viewer_decisions vd
              ON vd.viewer_profile_id = vp.id AND vd.media_record_id = @mid
            WHERE vp.is_active = 1
            ORDER BY vp.created_at";
        cmd.Parameters.AddWithValue("@mid", mediaRecordId);
        using var reader = cmd.ExecuteReader();
        var states = new List<ViewerProfileTagState>();
        while (reader.Read())
        {
            states.Add(new ViewerProfileTagState
            {
                ViewerProfileId = reader.GetString(0),
                State = reader.GetString(1),
                PendingTag = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                DeniedTag = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                AllowedTag = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
            });
        }

        return states;
    }

    private static IEnumerable<BaseItem> GetParentChain(BaseItem item)
    {
        var current = item.GetParent();
        while (current != null)
        {
            yield return current;
            current = current.GetParent();
        }
    }
}

public class SyncResult
{
    public int Imported { get; set; }
    public int Updated { get; set; }
    public int Deleted { get; set; }
    public int Errors { get; set; }
}
