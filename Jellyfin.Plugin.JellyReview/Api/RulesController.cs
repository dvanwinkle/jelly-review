using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Jellyfin.Api.Constants;
using Jellyfin.Plugin.JellyReview.Api.Dtos;
using Jellyfin.Plugin.JellyReview.Data;
using Jellyfin.Plugin.JellyReview.Models;
using Jellyfin.Plugin.JellyReview.Services;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyReview.Api;

[ApiController]
[Route("JellyReview/Rules")]
[Authorize(Policy = Policies.RequiresElevation)]
public class RulesController : ControllerBase
{
    private readonly DatabaseManager _db;
    private readonly RuleEngine _ruleEngine;
    private readonly ILibraryManager _libraryManager;

    public RulesController(DatabaseManager db, RuleEngine ruleEngine, ILibraryManager libraryManager)
    {
        _db = db;
        _ruleEngine = ruleEngine;
        _libraryManager = libraryManager;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<List<ReviewRuleDto>> GetRules()
    {
        var rules = LoadRules();
        var dtos = new List<ReviewRuleDto>();
        foreach (var r in rules)
            dtos.Add(MapRule(r));
        return Ok(dtos);
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult<ReviewRuleDto>> CreateRule([FromBody] CreateRuleRequest req)
    {
        var id = Guid.NewGuid().ToString();
        await _db.ExecuteWriteAsync(async conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO review_rules
                    (id, name, enabled, priority, conditions_json, action, viewer_profile_id, created_at, updated_at)
                VALUES (@id, @name, @enabled, @priority, @conditions, @action, @vpid, datetime('now'), datetime('now'))";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@name", req.Name);
            cmd.Parameters.AddWithValue("@enabled", req.Enabled ? 1 : 0);
            cmd.Parameters.AddWithValue("@priority", req.Priority);
            cmd.Parameters.AddWithValue("@conditions", req.ConditionsJson);
            cmd.Parameters.AddWithValue("@action", req.Action);
            cmd.Parameters.AddWithValue("@vpid", (object?)req.ViewerProfileId ?? DBNull.Value);
            cmd.ExecuteNonQuery();
            await Task.CompletedTask;
        });

        var rule = LoadRule(id);
        return CreatedAtAction(nameof(GetRules), MapRule(rule!));
    }

    [HttpPatch("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReviewRuleDto>> UpdateRule(string id, [FromBody] UpdateRuleRequest req)
    {
        var existing = LoadRule(id);
        if (existing == null) return NotFound();

        await _db.ExecuteWriteAsync(async conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE review_rules SET
                    name = @name, enabled = @enabled, priority = @priority,
                    conditions_json = @conditions, action = @action,
                    updated_at = datetime('now')
                WHERE id = @id";
            cmd.Parameters.AddWithValue("@name", req.Name ?? existing.Name);
            cmd.Parameters.AddWithValue("@enabled", (req.Enabled ?? existing.Enabled) ? 1 : 0);
            cmd.Parameters.AddWithValue("@priority", req.Priority ?? existing.Priority);
            cmd.Parameters.AddWithValue("@conditions", req.ConditionsJson ?? existing.ConditionsJson);
            cmd.Parameters.AddWithValue("@action", req.Action ?? existing.Action);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
            await Task.CompletedTask;
        });

        return Ok(MapRule(LoadRule(id)!));
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteRule(string id)
    {
        await _db.ExecuteWriteAsync(async conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM review_rules WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
            await Task.CompletedTask;
        });
        return NoContent();
    }

    [HttpPost("evaluate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<EvaluateRuleResult>> Evaluate([FromBody] EvaluateRuleRequest req)
    {
        // Build a MediaRecord from the Jellyfin item
        var record = GetMediaRecordForJellyfinItem(req.JellyfinItemId);
        if (record == null)
            return NotFound($"No media record found for Jellyfin item {req.JellyfinItemId}");

        var (action, matchedRule) = await _ruleEngine.EvaluateAsync(record, req.ViewerProfileId);
        return Ok(new EvaluateRuleResult
        {
            Action = action,
            MatchedRuleName = matchedRule?.Name,
            MatchedRuleId = matchedRule?.Id
        });
    }

    private MediaRecord? GetMediaRecordForJellyfinItem(string jellyfinItemId)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, jellyfin_item_id, media_type, title, sort_title, year,
                   official_rating, community_rating, runtime_minutes, overview,
                   genres_json, tags_snapshot_json, status
            FROM media_records WHERE jellyfin_item_id = @jid OR id = @jid LIMIT 1";
        cmd.Parameters.AddWithValue("@jid", jellyfinItemId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new MediaRecord
        {
            Id = r.GetString(0), JellyfinItemId = r.GetString(1), MediaType = r.GetString(2),
            Title = r.GetString(3), SortTitle = r.IsDBNull(4) ? null : r.GetString(4),
            Year = r.IsDBNull(5) ? null : r.GetInt32(5),
            OfficialRating = r.IsDBNull(6) ? null : r.GetString(6),
            CommunityRating = r.IsDBNull(7) ? null : r.GetDouble(7),
            RuntimeMinutes = r.IsDBNull(8) ? null : r.GetInt32(8),
            Overview = r.IsDBNull(9) ? null : r.GetString(9),
            GenresJson = r.IsDBNull(10) ? null : r.GetString(10),
            TagsSnapshotJson = r.IsDBNull(11) ? null : r.GetString(11),
            Status = r.GetString(12)
        };
    }

    private List<ReviewRule> LoadRules()
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, name, enabled, priority, conditions_json, action, viewer_profile_id, created_at, updated_at
            FROM review_rules ORDER BY priority ASC, created_at";
        using var r = cmd.ExecuteReader();
        var list = new List<ReviewRule>();
        while (r.Read())
            list.Add(ReadRule(r));
        return list;
    }

    private ReviewRule? LoadRule(string id)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, name, enabled, priority, conditions_json, action, viewer_profile_id, created_at, updated_at
            FROM review_rules WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadRule(r) : null;
    }

    private static ReviewRule ReadRule(Microsoft.Data.Sqlite.SqliteDataReader r) => new()
    {
        Id = r.GetString(0), Name = r.GetString(1), Enabled = r.GetInt32(2) == 1,
        Priority = r.GetInt32(3), ConditionsJson = r.GetString(4), Action = r.GetString(5),
        ViewerProfileId = r.IsDBNull(6) ? null : r.GetString(6),
        CreatedAt = r.GetString(7), UpdatedAt = r.GetString(8)
    };

    private static ReviewRuleDto MapRule(ReviewRule r) => new()
    {
        Id = r.Id, Name = r.Name, Enabled = r.Enabled, Priority = r.Priority,
        ConditionsJson = r.ConditionsJson, Action = r.Action,
        ViewerProfileId = r.ViewerProfileId, CreatedAt = r.CreatedAt
    };
}
