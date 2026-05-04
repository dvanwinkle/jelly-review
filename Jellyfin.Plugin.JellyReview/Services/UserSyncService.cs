using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyReview.Data;
using MediaBrowser.Controller.Library;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyReview.Services;

public record UserSyncResult(int DeactivatedProfiles, int DeletedRules);

public class UserSyncService
{
    private readonly DatabaseManager _db;
    private readonly IUserManager _userManager;
    private readonly ILogger<UserSyncService> _logger;

    public UserSyncService(DatabaseManager db, IUserManager userManager, ILogger<UserSyncService> logger)
    {
        _db = db;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<UserSyncResult> SyncDeletedUsersAsync()
    {
        var currentUserIds = _userManager.Users
            .Select(u => u.Id.ToString("N").ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return await _db.ExecuteWriteAsync(conn =>
        {
            var orphanedProfileIds = FindOrphanedProfileIds(conn, currentUserIds);
            if (orphanedProfileIds.Count == 0)
                return Task.FromResult(new UserSyncResult(0, 0));

            _logger.LogInformation("JellyReview: found {Count} orphaned viewer profiles", orphanedProfileIds.Count);

            var deletedRules = DeleteOrphanedRules(conn, orphanedProfileIds);
            PruneProfilesFromRules(conn, orphanedProfileIds);
            DeactivateProfiles(conn, orphanedProfileIds);

            _logger.LogInformation(
                "JellyReview: user sync — deactivated {Profiles} profiles, deleted {Rules} now-empty rules",
                orphanedProfileIds.Count, deletedRules);

            return Task.FromResult(new UserSyncResult(orphanedProfileIds.Count, deletedRules));
        }).ConfigureAwait(false);
    }

    private static List<string> FindOrphanedProfileIds(SqliteConnection conn, HashSet<string> currentUserIds)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, jellyfin_user_id
            FROM viewer_profiles
            WHERE is_active = 1 AND jellyfin_user_id IS NOT NULL";
        using var reader = cmd.ExecuteReader();

        var orphaned = new List<string>();
        while (reader.Read())
        {
            var profileId = reader.GetString(0);
            var rawUserId = reader.GetString(1);
            var normalized = Guid.TryParse(rawUserId, out var parsed)
                ? parsed.ToString("N").ToLowerInvariant()
                : rawUserId.ToLowerInvariant();

            if (!currentUserIds.Contains(normalized))
                orphaned.Add(profileId);
        }

        return orphaned;
    }

    private int DeleteOrphanedRules(SqliteConnection conn, List<string> orphanedProfileIds)
    {
        // Build a VALUES list so the orphaned IDs are referenced without repeating params.
        var valuesList = string.Join(",", orphanedProfileIds.Select((_, i) => $"(@p{i})"));

        // Rules whose every junction-table entry belongs to the orphaned set.
        using var findCmd = conn.CreateCommand();
        findCmd.CommandText = $@"
            WITH orphaned(id) AS (VALUES {valuesList})
            SELECT DISTINCT rrp.rule_id
            FROM review_rule_profiles rrp
            WHERE rrp.viewer_profile_id IN (SELECT id FROM orphaned)
              AND NOT EXISTS (
                  SELECT 1 FROM review_rule_profiles rrp2
                  WHERE rrp2.rule_id = rrp.rule_id
                    AND rrp2.viewer_profile_id NOT IN (SELECT id FROM orphaned)
              )";
        BindIndexed(findCmd, orphanedProfileIds, "p");

        var toDelete = new List<string>();
        using (var r = findCmd.ExecuteReader())
            while (r.Read()) toDelete.Add(r.GetString(0));

        // Legacy rules that only use the single viewer_profile_id column.
        using var legacyCmd = conn.CreateCommand();
        legacyCmd.CommandText = $@"
            WITH orphaned(id) AS (VALUES {valuesList})
            SELECT id FROM review_rules
            WHERE viewer_profile_id IN (SELECT id FROM orphaned)
              AND NOT EXISTS (
                  SELECT 1 FROM review_rule_profiles rrp WHERE rrp.rule_id = review_rules.id
              )";
        BindIndexed(legacyCmd, orphanedProfileIds, "p");
        using (var r = legacyCmd.ExecuteReader())
            while (r.Read())
            {
                var id = r.GetString(0);
                if (!toDelete.Contains(id, StringComparer.Ordinal))
                    toDelete.Add(id);
            }

        if (toDelete.Count == 0) return 0;

        var ruleValuesList = string.Join(",", toDelete.Select((_, i) => $"(@r{i})"));
        using var delCmd = conn.CreateCommand();
        delCmd.CommandText = $@"
            WITH targets(id) AS (VALUES {ruleValuesList})
            DELETE FROM review_rules WHERE id IN (SELECT id FROM targets)";
        BindIndexed(delCmd, toDelete, "r");
        delCmd.ExecuteNonQuery();

        return toDelete.Count;
    }

    private static void PruneProfilesFromRules(SqliteConnection conn, List<string> orphanedProfileIds)
    {
        var valuesList = string.Join(",", orphanedProfileIds.Select((_, i) => $"(@p{i})"));

        using var junctionCmd = conn.CreateCommand();
        junctionCmd.CommandText = $@"
            WITH orphaned(id) AS (VALUES {valuesList})
            DELETE FROM review_rule_profiles WHERE viewer_profile_id IN (SELECT id FROM orphaned)";
        BindIndexed(junctionCmd, orphanedProfileIds, "p");
        junctionCmd.ExecuteNonQuery();

        using var legacyCmd = conn.CreateCommand();
        legacyCmd.CommandText = $@"
            WITH orphaned(id) AS (VALUES {valuesList})
            UPDATE review_rules
            SET viewer_profile_id = NULL, updated_at = datetime('now')
            WHERE viewer_profile_id IN (SELECT id FROM orphaned)";
        BindIndexed(legacyCmd, orphanedProfileIds, "p");
        legacyCmd.ExecuteNonQuery();
    }

    private static void DeactivateProfiles(SqliteConnection conn, List<string> orphanedProfileIds)
    {
        var valuesList = string.Join(",", orphanedProfileIds.Select((_, i) => $"(@p{i})"));
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            WITH orphaned(id) AS (VALUES {valuesList})
            UPDATE viewer_profiles
            SET is_active = 0, updated_at = datetime('now')
            WHERE id IN (SELECT id FROM orphaned)";
        BindIndexed(cmd, orphanedProfileIds, "p");
        cmd.ExecuteNonQuery();
    }

    private static void BindIndexed(SqliteCommand cmd, List<string> values, string prefix)
    {
        for (var i = 0; i < values.Count; i++)
            cmd.Parameters.AddWithValue($"@{prefix}{i}", values[i]);
    }
}
