using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.JellyReview.Services.Notifications;

public class SlackProvider : INotificationProvider
{
    private readonly Dictionary<string, object> _config;

    public SlackProvider(Dictionary<string, object> config)
    {
        _config = config;
    }

    private string WebhookUrl => _config.TryGetValue("webhook_url", out var v) ? v.ToString()! : string.Empty;

    public async Task<DeliveryResult> TestConnectionAsync()
    {
        try
        {
            using var client = new HttpClient();
            var body = JsonSerializer.Serialize(new { text = "JellyReview notification test — connection successful!" });
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
        var overview = payload.Overview ?? "No overview available";
        if (overview.Length > 200) overview = overview[..200];
        var rating = payload.OfficialRating ?? "NR";
        var genres = payload.Genres.Take(3) is var g && g.Any() ? string.Join(", ", g) : "Unknown";

        var blocks = new List<object>
        {
            new
            {
                type = "section",
                text = new { type = "mrkdwn", text = $"*Review Needed: {payload.Title}* ({payload.Year?.ToString() ?? "Unknown"})\n{rating} · {payload.MediaType} · {genres}" },
                accessory = new { type = "image", image_url = payload.PosterProxyUrl, alt_text = payload.Title }
            },
            new { type = "section", text = new { type = "mrkdwn", text = overview } },
        };

        if (payload.RequestedBy != null)
            blocks.Add(new { type = "context", elements = new[] { new { type = "mrkdwn", text = $"Requested by *{payload.RequestedBy}*" } } });

        blocks.Add(new
        {
            type = "actions",
            elements = new object[]
            {
                new { type = "button", text = new { type = "plain_text", text = "Approve" }, url = payload.ApproveTokenUrl, style = "primary" },
                new { type = "button", text = new { type = "plain_text", text = "Deny" }, url = payload.DenyTokenUrl, style = "danger" },
                new { type = "button", text = new { type = "plain_text", text = "Defer" }, url = payload.DeferTokenUrl },
                new { type = "button", text = new { type = "plain_text", text = "Details" }, url = payload.DetailUrl },
            }
        });

        try
        {
            using var client = new HttpClient();
            var body = JsonSerializer.Serialize(new { blocks });
            var resp = await client.PostAsync(WebhookUrl, new StringContent(body, Encoding.UTF8, "application/json")).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            return new DeliveryResult { Success = true };
        }
        catch (Exception ex)
        {
            return new DeliveryResult { Success = false, Error = ex.Message };
        }
    }

    public Task<DeliveryResult> SendActionConfirmationAsync(string title, string action, string? actor = null)
        => Task.FromResult(new DeliveryResult { Success = true });
}
