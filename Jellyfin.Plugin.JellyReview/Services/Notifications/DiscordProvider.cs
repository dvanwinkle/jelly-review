using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.JellyReview.Services.Notifications;

public class DiscordProvider : INotificationProvider
{
    private readonly Dictionary<string, object> _config;

    private static readonly Dictionary<string, int> RatingColors = new()
    {
        ["G"] = 0x2ECC71, ["PG"] = 0x27AE60,
        ["TV-Y"] = 0x2ECC71, ["TV-Y7"] = 0x2ECC71,
        ["TV-G"] = 0x27AE60, ["TV-PG"] = 0xF1C40F,
        ["PG-13"] = 0xE67E22, ["TV-14"] = 0xE67E22,
        ["R"] = 0xE74C3C, ["NC-17"] = 0xC0392B, ["TV-MA"] = 0xC0392B,
    };
    private const int DefaultColor = 0x5865F2;

    public DiscordProvider(Dictionary<string, object> config)
    {
        _config = config;
    }

    private string WebhookUrl => _config.TryGetValue("webhook_url", out var v) ? v.ToString()! : string.Empty;
    private string Username => _config.TryGetValue("username", out var v) ? v.ToString()! : "JellyReview";

    public async Task<DeliveryResult> TestConnectionAsync()
    {
        try
        {
            using var client = new HttpClient();
            var body = JsonSerializer.Serialize(new { username = Username, content = "JellyReview notification test — connection successful!" });
            var resp = await client.PostAsync(WebhookUrl, new StringContent(body, Encoding.UTF8, "application/json")).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            return new DeliveryResult { Success = true };
        }
        catch (Exception ex)
        {
            return new DeliveryResult { Success = false, Error = ex.Message };
        }
    }

    public async Task<DeliveryResult> SendReviewNotificationAsync(ReviewNotificationPayload payload)
    {
        var color = RatingColors.GetValueOrDefault(payload.OfficialRating ?? string.Empty, DefaultColor);
        var genres = payload.Genres.Take(3) is var g && g.Any() ? string.Join(", ", g) : "Unknown";
        var overview = payload.Overview ?? "No overview available";
        if (overview.Length > 300) overview = overview[..300] + "…";

        var actionText = $"[Approve]({payload.ApproveTokenUrl})  [Deny]({payload.DenyTokenUrl})  [Defer]({payload.DeferTokenUrl})  [Details]({payload.DetailUrl})";
        var description = $"{overview}\n\n{actionText}";

        var fields = new List<object>
        {
            new { name = "Rating", value = payload.OfficialRating ?? "NR", inline = true },
            new { name = "Type", value = payload.MediaType, inline = true },
            new { name = "Year", value = payload.Year?.ToString() ?? "Unknown", inline = true },
            new { name = "Genres", value = genres, inline = false },
        };
        if (payload.RequestedBy != null)
            fields.Add(new { name = "Requested by", value = payload.RequestedBy, inline = true });
        if (payload.PendingCount > 1)
            fields.Add(new { name = "Queue", value = $"{payload.PendingCount} items pending", inline = true });

        var embed = new
        {
            title = $"Review Needed: {payload.Title}",
            description,
            color,
            fields,
            thumbnail = new { url = payload.PosterProxyUrl },
            footer = new { text = "JellyReview" }
        };

        var body = JsonSerializer.Serialize(new { username = Username, embeds = new[] { embed } });

        try
        {
            using var client = new HttpClient();
            var resp = await client.PostAsync(WebhookUrl, new StringContent(body, Encoding.UTF8, "application/json")).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            return new DeliveryResult { Success = true };
        }
        catch (Exception ex)
        {
            return new DeliveryResult { Success = false, Error = ex.Message };
        }
    }

    public async Task<DeliveryResult> SendActionConfirmationAsync(string title, string action, string? actor = null)
    {
        var msg = $"**{title}** was **{action}d**" + (actor != null ? $" by {actor}" : string.Empty);
        try
        {
            using var client = new HttpClient();
            var body = JsonSerializer.Serialize(new { username = Username, content = msg });
            var resp = await client.PostAsync(WebhookUrl, new StringContent(body, Encoding.UTF8, "application/json")).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            return new DeliveryResult { Success = true };
        }
        catch (Exception ex)
        {
            return new DeliveryResult { Success = false, Error = ex.Message };
        }
    }
}
