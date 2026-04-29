using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.JellyReview.Data;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.JellyReview.Services;

public class ParentalControlService
{
    private readonly DatabaseManager _db;
    private readonly IUserManager _userManager;

    public ParentalControlService(DatabaseManager db, IUserManager userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public async Task ApplyTagPreferencesAsync(Guid jellyfinUserId)
    {
        var user = _userManager.GetUserById(jellyfinUserId);
        if (user is null) return;

        var config = Plugin.Instance!.Configuration;
        var profiles = LoadProfilesForJellyfinUser(jellyfinUserId.ToString());
        if (profiles.Count == 0) return;

        var blockedTags = GetPreferenceTags(user, PreferenceKind.BlockedTags);
        RemoveTags(blockedTags, config.PendingTag, config.DeniedTag, config.AllowedTag);
        AddMissingTags(blockedTags, profiles.SelectMany(p => new[] { p.PendingTag, p.DeniedTag }).ToArray());
        user.SetPreference(PreferenceKind.BlockedTags, blockedTags.ToArray());

        var allowedTags = GetPreferenceTags(user, PreferenceKind.AllowedTags);
        RemoveTags(allowedTags, config.PendingTag, config.DeniedTag, config.AllowedTag);
        AddMissingTags(allowedTags, profiles.Select(p => p.AllowedTag).ToArray());
        user.SetPreference(PreferenceKind.AllowedTags, allowedTags.ToArray());

        await _userManager.UpdateUserAsync(user).ConfigureAwait(false);
    }

    public async Task ApplyTagPreferencesForActiveProfilesAsync()
    {
        foreach (var jellyfinUserId in LoadActiveJellyfinUserIds())
        {
            if (Guid.TryParse(jellyfinUserId, out var userGuid))
                await ApplyTagPreferencesAsync(userGuid).ConfigureAwait(false);
        }
    }

    private List<string> LoadActiveJellyfinUserIds()
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT jellyfin_user_id
            FROM viewer_profiles
            WHERE is_active = 1
              AND jellyfin_user_id IS NOT NULL
              AND jellyfin_user_id <> ''";

        using var reader = cmd.ExecuteReader();
        var ids = new List<string>();
        while (reader.Read())
            ids.Add(reader.GetString(0));
        return ids;
    }

    private List<(string PendingTag, string DeniedTag, string AllowedTag)> LoadProfilesForJellyfinUser(string jellyfinUserId)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT pending_tag, denied_tag, allowed_tag
            FROM viewer_profiles
            WHERE is_active = 1 AND jellyfin_user_id = @id";
        cmd.Parameters.AddWithValue("@id", jellyfinUserId);
        using var reader = cmd.ExecuteReader();
        var profiles = new List<(string PendingTag, string DeniedTag, string AllowedTag)>();
        while (reader.Read())
        {
            profiles.Add((
                reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                reader.IsDBNull(2) ? string.Empty : reader.GetString(2)));
        }

        return profiles;
    }

    private static List<string> GetPreferenceTags(User user, PreferenceKind kind)
    {
        return user.GetPreference(kind)
            .SelectMany(p => p.Split('|', StringSplitOptions.RemoveEmptyEntries))
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();
    }

    private static void AddMissingTags(List<string> tags, params string[] tagsToAdd)
    {
        foreach (var tag in tagsToAdd)
        {
            if (!string.IsNullOrWhiteSpace(tag) && !tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                tags.Add(tag);
        }
    }

    private static void RemoveTags(List<string> tags, params string[] tagsToRemove)
    {
        foreach (var tag in tagsToRemove.Where(t => !string.IsNullOrWhiteSpace(t)))
            tags.RemoveAll(existing => string.Equals(existing, tag, StringComparison.OrdinalIgnoreCase));
    }
}
