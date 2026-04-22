using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyReview.Data;
using Jellyfin.Plugin.JellyReview.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyReview.Services;

public class RuleEngine
{
    private readonly DatabaseManager _db;
    private readonly ILogger<RuleEngine> _logger;

    public RuleEngine(DatabaseManager db, ILogger<RuleEngine> logger)
    {
        _db = db;
        _logger = logger;
    }

    // Port of Python's normalize_official_rating
    public static string? NormalizeOfficialRating(string? rating)
    {
        if (string.IsNullOrWhiteSpace(rating)) return null;

        var normalized = rating.Trim().ToUpperInvariant()
            .Replace("_", "-")
            .Replace(" ", "-");

        while (normalized.Contains("--"))
            normalized = normalized.Replace("--", "-");

        // Sorted longest-first to prevent partial matches (e.g. TV-Y before TV-Y7)
        string[] baseRatings =
        {
            "NC-17", "PG-13", "TV-MA", "TV-Y7", "TV-14", "TV-PG", "TV-G", "TV-Y",
            "NR", "UR", "PG", "G", "R", "X"
        };

        foreach (var base_ in baseRatings)
        {
            if (normalized == base_ || normalized.StartsWith(base_ + "-", StringComparison.Ordinal))
                return base_;
        }

        return normalized;
    }

    private static bool RatingMatches(string ruleRating, string? itemRating)
    {
        var nr = NormalizeOfficialRating(ruleRating);
        var ni = NormalizeOfficialRating(itemRating);
        return nr != null && nr == ni;
    }

    private static bool ItemMatchesConditions(MediaRecord item, JsonElement conditions)
    {
        if (conditions.TryGetProperty("official_rating_in", out var ratingIn))
        {
            bool any = false;
            foreach (var r in ratingIn.EnumerateArray())
                if (RatingMatches(r.GetString()!, item.OfficialRating)) { any = true; break; }
            if (!any) return false;
        }

        if (conditions.TryGetProperty("official_rating_not_in", out var ratingNotIn))
        {
            foreach (var r in ratingNotIn.EnumerateArray())
                if (RatingMatches(r.GetString()!, item.OfficialRating)) return false;
        }

        if (conditions.TryGetProperty("media_type_in", out var typeIn))
        {
            bool found = false;
            foreach (var t in typeIn.EnumerateArray())
                if (string.Equals(t.GetString(), item.MediaType, StringComparison.OrdinalIgnoreCase))
                { found = true; break; }
            if (!found) return false;
        }

        if (conditions.TryGetProperty("community_rating_gte", out var ratingGte))
        {
            if (item.CommunityRating == null || item.CommunityRating < ratingGte.GetDouble())
                return false;
        }

        if (conditions.TryGetProperty("community_rating_lte", out var ratingLte))
        {
            if (item.CommunityRating == null || item.CommunityRating > ratingLte.GetDouble())
                return false;
        }

        if (conditions.TryGetProperty("genre_in", out var genreIn))
        {
            List<string> itemGenres = new();
            if (!string.IsNullOrEmpty(item.GenresJson))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<List<string>>(item.GenresJson);
                    if (parsed != null) itemGenres = parsed;
                }
                catch { /* malformed JSON, treat as empty */ }
            }

            bool found = false;
            foreach (var g in genreIn.EnumerateArray())
                if (itemGenres.Contains(g.GetString()!)) { found = true; break; }
            if (!found) return false;
        }

        return true;
    }

    public async Task<(string? Action, ReviewRule? MatchedRule)> EvaluateAsync(
        MediaRecord item, string? viewerProfileId = null)
    {
        using var conn = _db.CreateConnection();

        if (viewerProfileId != null)
        {
            var profileRules = LoadRules(conn, viewerProfileId, global: false);
            foreach (var rule in profileRules)
            {
                if (TryMatchRule(rule, item, out var action))
                    return (action, rule);
            }
        }

        var globalRules = LoadRules(conn, viewerProfileId: null, global: true);
        foreach (var rule in globalRules)
        {
            if (TryMatchRule(rule, item, out var action))
                return (action, rule);
        }

        return (null, null);
    }

    private bool TryMatchRule(ReviewRule rule, MediaRecord item, out string? action)
    {
        action = null;
        try
        {
            var conditions = JsonSerializer.Deserialize<JsonElement>(rule.ConditionsJson);
            if (ItemMatchesConditions(item, conditions))
            {
                action = rule.Action;
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "JellyReview: failed to parse conditions for rule {RuleId}", rule.Id);
        }
        return false;
    }

    private static List<ReviewRule> LoadRules(SqliteConnection conn, string? viewerProfileId, bool global)
    {
        var rules = new List<ReviewRule>();
        using var cmd = conn.CreateCommand();

        if (global)
        {
            cmd.CommandText = @"
                SELECT id, name, enabled, priority, conditions_json, action, viewer_profile_id
                FROM review_rules
                WHERE enabled = 1 AND viewer_profile_id IS NULL
                ORDER BY priority ASC";
        }
        else
        {
            cmd.CommandText = @"
                SELECT id, name, enabled, priority, conditions_json, action, viewer_profile_id
                FROM review_rules
                WHERE enabled = 1 AND viewer_profile_id = @vpid
                ORDER BY priority ASC";
            cmd.Parameters.AddWithValue("@vpid", viewerProfileId!);
        }

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rules.Add(new ReviewRule
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                Enabled = reader.GetInt32(2) == 1,
                Priority = reader.GetInt32(3),
                ConditionsJson = reader.GetString(4),
                Action = reader.GetString(5),
                ViewerProfileId = reader.IsDBNull(6) ? null : reader.GetString(6)
            });
        }

        return rules;
    }

    public async Task SeedStarterRulesAsync()
    {
        using var conn = _db.CreateConnection();
        using var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM review_rules";
        var count = (long)(countCmd.ExecuteScalar() ?? 0L);
        if (count > 0) return;

        await _db.ExecuteWriteAsync(async writeConn =>
        {
            var starters = new[]
            {
                ("Auto-approve safe ratings", 10, true,
                    """{"official_rating_in":["G","PG","TV-Y","TV-Y7","TV-G","TV-PG"]}""",
                    "auto_approve"),
                ("Send teen ratings to manual review", 20, true,
                    """{"official_rating_in":["PG-13","TV-14"]}""",
                    "send_to_review"),
                ("Auto-deny adult content", 30, true,
                    """{"official_rating_in":["R","NC-17","TV-MA","X"]}""",
                    "auto_deny"),
            };

            foreach (var (name, priority, enabled, conditions, action) in starters)
            {
                using var cmd = writeConn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO review_rules (id, name, enabled, priority, conditions_json, action)
                    VALUES (@id, @name, @enabled, @priority, @conditions, @action)";
                cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
                cmd.Parameters.AddWithValue("@name", name);
                cmd.Parameters.AddWithValue("@enabled", enabled ? 1 : 0);
                cmd.Parameters.AddWithValue("@priority", priority);
                cmd.Parameters.AddWithValue("@conditions", conditions);
                cmd.Parameters.AddWithValue("@action", action);
                cmd.ExecuteNonQuery();
            }

            await Task.CompletedTask;
        });
    }
}
