using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellyReview.Data;
using Jellyfin.Plugin.JellyReview.Models;
using Jellyfin.Plugin.JellyReview.Services.Notifications;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyReview.Services;

public class NotificationService
{
    private readonly DatabaseManager _db;
    private readonly ILogger<NotificationService> _logger;
    private const int ActionTokenMaxAgeSeconds = 72 * 3600;

    private static readonly Dictionary<string, Func<Dictionary<string, object>, INotificationProvider>> ProviderMap = new()
    {
        ["discord"] = cfg => new DiscordProvider(cfg),
        ["ntfy"] = cfg => new NtfyProvider(cfg),
        ["slack"] = cfg => new SlackProvider(cfg),
        ["pushover"] = cfg => new PushoverProvider(cfg),
        ["smtp"] = cfg => new SmtpProvider(cfg),
        ["webhook"] = cfg => new WebhookProvider(cfg),
    };

    public NotificationService(DatabaseManager db, ILogger<NotificationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // --- Action Token ---

    public string CreateActionToken(string mediaRecordId, string action, string? viewerProfileId = null)
    {
        var payload = $"{mediaRecordId}|{action}|{viewerProfileId ?? string.Empty}|{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var key = GetOrCreateSigningKey();
        using var hmac = new HMACSHA256(key);
        var sig = hmac.ComputeHash(payloadBytes);
        return Base64UrlEncode(payloadBytes) + "." + Base64UrlEncode(sig);
    }

    public (string? MediaRecordId, string? Action, string? ViewerProfileId) VerifyActionToken(string token)
    {
        try
        {
            var dot = token.LastIndexOf('.');
            if (dot < 0) return (null, null, null);

            var payloadBytes = Base64UrlDecode(token[..dot]);
            var sig = Base64UrlDecode(token[(dot + 1)..]);

            var key = GetOrCreateSigningKey();
            using var hmac = new HMACSHA256(key);
            var expected = hmac.ComputeHash(payloadBytes);

            if (!CryptographicOperations.FixedTimeEquals(sig, expected))
                return (null, null, null);

            var payload = Encoding.UTF8.GetString(payloadBytes);
            var parts = payload.Split('|');
            if (parts.Length < 4) return (null, null, null);

            var issuedAt = long.Parse(parts[3]);
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - issuedAt > ActionTokenMaxAgeSeconds)
                return (null, null, null);

            return (parts[0], parts[1], string.IsNullOrEmpty(parts[2]) ? null : parts[2]);
        }
        catch
        {
            return (null, null, null);
        }
    }

    // --- Notification Dispatch ---

    public async Task<List<object>> NotifyPendingReviewAsync(string mediaRecordId, string? viewerProfileId = null)
        => await DispatchAsync(mediaRecordId, "pending_review", viewerProfileId,
            requirePending: true, requireConflict: false).ConfigureAwait(false);

    public async Task<List<object>> NotifyConflictAsync(string mediaRecordId, string? viewerProfileId = null)
        => await DispatchAsync(mediaRecordId, "conflict", viewerProfileId,
            requirePending: false, requireConflict: true).ConfigureAwait(false);

    private async Task<List<object>> DispatchAsync(
        string mediaRecordId, string triggerEvent, string? viewerProfileId,
        bool requirePending, bool requireConflict)
    {
        var payload = BuildPayload(mediaRecordId, viewerProfileId);
        if (payload == null) return new List<object>();

        var channels = LoadChannels(requirePending, requireConflict);
        var results = new List<object>();

        foreach (var channel in channels)
        {
            if (!ProviderMap.TryGetValue(channel.ProviderType, out var factory)) continue;

            Dictionary<string, object> config;
            try
            {
                config = string.IsNullOrEmpty(channel.ConfigJson)
                    ? new Dictionary<string, object>()
                    : JsonSerializer.Deserialize<Dictionary<string, object>>(channel.ConfigJson)
                      ?? new Dictionary<string, object>();
            }
            catch
            {
                config = new Dictionary<string, object>();
            }

            var provider = factory(config);
            DeliveryResult result;
            try
            {
                result = await provider.SendReviewNotificationAsync(payload).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                result = new DeliveryResult { Success = false, Error = ex.Message };
            }

            // Record delivery
            var approveToken = CreateActionToken(mediaRecordId, "approve", viewerProfileId);
            await _db.ExecuteWriteAsync(async conn =>
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO notification_deliveries
                        (id, channel_id, media_record_id, trigger_event, status,
                         provider_message_id, action_token_hash, created_at)
                    VALUES (@id, @cid, @mid, @event, @status, @pmid, @hash, datetime('now'))";
                cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
                cmd.Parameters.AddWithValue("@cid", channel.Id);
                cmd.Parameters.AddWithValue("@mid", mediaRecordId);
                cmd.Parameters.AddWithValue("@event", triggerEvent);
                cmd.Parameters.AddWithValue("@status", result.Success ? "sent" : "failed");
                cmd.Parameters.AddWithValue("@pmid", (object?)result.ProviderMessageId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@hash", ComputeTokenHash(approveToken));
                cmd.ExecuteNonQuery();
                await Task.CompletedTask;
            }).ConfigureAwait(false);

            results.Add(new { channel = channel.Name, success = result.Success, error = result.Error });
            _logger.LogInformation("JellyReview: notification {Event} via {Channel}: {Status}",
                triggerEvent, channel.Name, result.Success ? "sent" : "failed");
        }

        return results;
    }

    public async Task<DeliveryResult> TestChannelAsync(string channelId)
    {
        var channel = GetChannel(channelId);
        if (channel == null) return new DeliveryResult { Success = false, Error = "Channel not found" };

        if (!ProviderMap.TryGetValue(channel.ProviderType, out var factory))
            return new DeliveryResult { Success = false, Error = $"Unknown provider: {channel.ProviderType}" };

        try
        {
            var config = string.IsNullOrEmpty(channel.ConfigJson)
                ? new Dictionary<string, object>()
                : JsonSerializer.Deserialize<Dictionary<string, object>>(channel.ConfigJson)
                  ?? new Dictionary<string, object>();
            var provider = factory(config);
            return await provider.TestConnectionAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new DeliveryResult { Success = false, Error = ex.Message };
        }
    }

    // --- CRUD helpers ---

    public NotificationChannel? GetChannel(string id)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, name, provider_type, config_json, enabled,
                   notify_on_pending, notify_on_conflict, notify_on_digest, created_at, updated_at
            FROM notification_channels WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadChannel(r) : null;
    }

    public List<NotificationChannel> GetChannels()
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, name, provider_type, config_json, enabled,
                   notify_on_pending, notify_on_conflict, notify_on_digest, created_at, updated_at
            FROM notification_channels ORDER BY created_at";
        using var r = cmd.ExecuteReader();
        var list = new List<NotificationChannel>();
        while (r.Read()) list.Add(ReadChannel(r));
        return list;
    }

    public async Task<NotificationChannel> CreateChannelAsync(
        string name, string providerType, Dictionary<string, object> config,
        bool notifyOnPending = true, bool notifyOnConflict = true)
    {
        var channel = new NotificationChannel
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            ProviderType = providerType,
            ConfigJson = JsonSerializer.Serialize(config),
            Enabled = true,
            NotifyOnPending = notifyOnPending,
            NotifyOnConflict = notifyOnConflict,
            CreatedAt = DateTime.UtcNow.ToString("O"),
            UpdatedAt = DateTime.UtcNow.ToString("O")
        };

        await _db.ExecuteWriteAsync(async conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO notification_channels
                    (id, name, provider_type, config_json, enabled,
                     notify_on_pending, notify_on_conflict, created_at, updated_at)
                VALUES (@id, @name, @type, @cfg, @enabled, @pending, @conflict, @created, @updated)";
            cmd.Parameters.AddWithValue("@id", channel.Id);
            cmd.Parameters.AddWithValue("@name", channel.Name);
            cmd.Parameters.AddWithValue("@type", channel.ProviderType);
            cmd.Parameters.AddWithValue("@cfg", channel.ConfigJson!);
            cmd.Parameters.AddWithValue("@enabled", channel.Enabled ? 1 : 0);
            cmd.Parameters.AddWithValue("@pending", channel.NotifyOnPending ? 1 : 0);
            cmd.Parameters.AddWithValue("@conflict", channel.NotifyOnConflict ? 1 : 0);
            cmd.Parameters.AddWithValue("@created", channel.CreatedAt);
            cmd.Parameters.AddWithValue("@updated", channel.UpdatedAt);
            cmd.ExecuteNonQuery();
            await Task.CompletedTask;
        }).ConfigureAwait(false);

        return channel;
    }

    public async Task UpdateChannelAsync(string id, string? name, bool? enabled,
        Dictionary<string, object>? config, bool? notifyOnPending, bool? notifyOnConflict)
    {
        await _db.ExecuteWriteAsync(async conn =>
        {
            var ch = GetChannel(id);
            if (ch == null) return;

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE notification_channels SET
                    name = @name, enabled = @enabled,
                    config_json = @cfg,
                    notify_on_pending = @pending, notify_on_conflict = @conflict,
                    updated_at = datetime('now')
                WHERE id = @id";
            cmd.Parameters.AddWithValue("@name", name ?? ch.Name);
            cmd.Parameters.AddWithValue("@enabled", (enabled ?? ch.Enabled) ? 1 : 0);
            cmd.Parameters.AddWithValue("@cfg", config != null ? JsonSerializer.Serialize(config) : (ch.ConfigJson ?? "{}"));
            cmd.Parameters.AddWithValue("@pending", (notifyOnPending ?? ch.NotifyOnPending) ? 1 : 0);
            cmd.Parameters.AddWithValue("@conflict", (notifyOnConflict ?? ch.NotifyOnConflict) ? 1 : 0);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
            await Task.CompletedTask;
        }).ConfigureAwait(false);
    }

    public async Task DeleteChannelAsync(string id)
    {
        await _db.ExecuteWriteAsync(async conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM notification_channels WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
            await Task.CompletedTask;
        }).ConfigureAwait(false);
    }

    // --- Private helpers ---

    private ReviewNotificationPayload? BuildPayload(string mediaRecordId, string? viewerProfileId)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = !string.IsNullOrEmpty(viewerProfileId)
            ? @"
            SELECT mr.id, mr.title, mr.year, mr.media_type, mr.official_rating, mr.overview, mr.genres_json,
                   (SELECT COUNT(*) FROM viewer_decisions WHERE state='pending' AND viewer_profile_id = @vpid) as pending_count
            FROM media_records mr WHERE mr.id = @id"
            : @"
            SELECT mr.id, mr.title, mr.year, mr.media_type, mr.official_rating, mr.overview, mr.genres_json,
                   (SELECT CASE
                       WHEN EXISTS (SELECT 1 FROM viewer_decisions)
                       THEN (SELECT COUNT(*) FROM viewer_decisions WHERE state='pending')
                       ELSE (SELECT COUNT(*) FROM review_decisions WHERE state='pending')
                   END) as pending_count
            FROM media_records mr WHERE mr.id = @id";
        cmd.Parameters.AddWithValue("@id", mediaRecordId);
        if (!string.IsNullOrEmpty(viewerProfileId))
            cmd.Parameters.AddWithValue("@vpid", viewerProfileId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;

        var id = r.GetString(0);
        var title = r.GetString(1);
        var year = r.IsDBNull(2) ? (int?)null : r.GetInt32(2);
        var mediaType = r.GetString(3);
        var rating = r.IsDBNull(4) ? null : r.GetString(4);
        var overview = r.IsDBNull(5) ? null : r.GetString(5);
        var genresJson = r.IsDBNull(6) ? null : r.GetString(6);
        var pendingCount = r.GetInt32(7);

        List<string> genres = new();
        if (!string.IsNullOrEmpty(genresJson))
        {
            try { genres = JsonSerializer.Deserialize<List<string>>(genresJson) ?? new(); }
            catch { }
        }

        var baseUrl = "http://localhost:8096"; // Jellyfin base — action tokens are self-contained
        return new ReviewNotificationPayload
        {
            MediaRecordId = id,
            Title = title,
            Year = year,
            MediaType = mediaType,
            OfficialRating = rating,
            Overview = overview,
            Genres = genres,
            PendingCount = pendingCount,
            PosterProxyUrl = $"{baseUrl}/JellyReview/Media/{id}/poster",
            ApproveTokenUrl = $"{baseUrl}/JellyReview/Action/{CreateActionToken(id, "approve", viewerProfileId)}",
            DenyTokenUrl = $"{baseUrl}/JellyReview/Action/{CreateActionToken(id, "deny", viewerProfileId)}",
            DeferTokenUrl = $"{baseUrl}/JellyReview/Action/{CreateActionToken(id, "defer", viewerProfileId)}",
            DetailUrl = $"{baseUrl}/web/index.html#!/jellyreview"
        };
    }

    private List<NotificationChannel> LoadChannels(bool forPending, bool forConflict)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();

        if (forPending)
            cmd.CommandText = @"
                SELECT id, name, provider_type, config_json, enabled,
                       notify_on_pending, notify_on_conflict, notify_on_digest, created_at, updated_at
                FROM notification_channels WHERE enabled = 1 AND notify_on_pending = 1";
        else
            cmd.CommandText = @"
                SELECT id, name, provider_type, config_json, enabled,
                       notify_on_pending, notify_on_conflict, notify_on_digest, created_at, updated_at
                FROM notification_channels WHERE enabled = 1 AND notify_on_conflict = 1";

        using var r = cmd.ExecuteReader();
        var list = new List<NotificationChannel>();
        while (r.Read()) list.Add(ReadChannel(r));
        return list;
    }

    private static NotificationChannel ReadChannel(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        Name = r.GetString(1),
        ProviderType = r.GetString(2),
        ConfigJson = r.IsDBNull(3) ? null : r.GetString(3),
        Enabled = r.GetInt32(4) == 1,
        NotifyOnPending = r.GetInt32(5) == 1,
        NotifyOnConflict = r.GetInt32(6) == 1,
        NotifyOnDigest = r.GetInt32(7) == 1,
        CreatedAt = r.GetString(8),
        UpdatedAt = r.GetString(9)
    };

    private byte[] GetOrCreateSigningKey()
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM app_secrets WHERE key = 'action_token_key'";
        var existing = cmd.ExecuteScalar()?.ToString();
        if (!string.IsNullOrEmpty(existing))
            return Convert.FromBase64String(existing);

        var key = RandomNumberGenerator.GetBytes(32);
        var encoded = Convert.ToBase64String(key);

        _db.WriteLock.Wait();
        try
        {
            using var writeConn = _db.CreateConnection();
            using var insertCmd = writeConn.CreateCommand();
            insertCmd.CommandText = @"
                INSERT OR IGNORE INTO app_secrets (key, value)
                VALUES ('action_token_key', @val)";
            insertCmd.Parameters.AddWithValue("@val", encoded);
            insertCmd.ExecuteNonQuery();
        }
        finally
        {
            _db.WriteLock.Release();
        }

        return key;
    }

    private static string ComputeTokenHash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Base64UrlEncode(byte[] data)
        => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
        return Convert.FromBase64String(s);
    }
}
