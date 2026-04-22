using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.JellyReview.Services.Notifications;

public class PushoverProvider : INotificationProvider
{
    private const string ApiUrl = "https://api.pushover.net/1/messages.json";
    private readonly Dictionary<string, object> _config;

    public PushoverProvider(Dictionary<string, object> config)
    {
        _config = config;
    }

    private string AppToken => _config.TryGetValue("app_token", out var v) ? v.ToString()! : string.Empty;
    private string UserKey => _config.TryGetValue("user_key", out var v) ? v.ToString()! : string.Empty;
    private string? Device => _config.TryGetValue("device", out var v) ? v.ToString() : null;

    public async Task<DeliveryResult> TestConnectionAsync()
    {
        var form = new Dictionary<string, string>
        {
            ["token"] = AppToken, ["user"] = UserKey,
            ["message"] = "JellyReview test — connection successful!", ["title"] = "JellyReview"
        };
        return await Post(form).ConfigureAwait(false);
    }

    public async Task<DeliveryResult> SendReviewNotificationAsync(ReviewNotificationPayload payload)
    {
        var rating = payload.OfficialRating ?? "NR";
        var message = $"{payload.MediaType} · {rating}\n{(payload.Overview ?? string.Empty)[..Math.Min(150, payload.Overview?.Length ?? 0)]}\n\nTap to review in JellyReview.";
        var form = new Dictionary<string, string>
        {
            ["token"] = AppToken, ["user"] = UserKey,
            ["title"] = $"Review: {payload.Title}",
            ["message"] = message,
            ["url"] = payload.DetailUrl,
            ["url_title"] = "Open in JellyReview",
            ["priority"] = "0"
        };
        if (Device != null) form["device"] = Device;
        return await Post(form).ConfigureAwait(false);
    }

    public Task<DeliveryResult> SendActionConfirmationAsync(string title, string action, string? actor = null)
        => Task.FromResult(new DeliveryResult { Success = true });

    private static async Task<DeliveryResult> Post(Dictionary<string, string> form)
    {
        try
        {
            using var client = new HttpClient();
            var resp = await client.PostAsync(ApiUrl, new FormUrlEncodedContent(form)).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            return new DeliveryResult { Success = true };
        }
        catch (Exception ex)
        {
            return new DeliveryResult { Success = false, Error = ex.Message };
        }
    }
}
