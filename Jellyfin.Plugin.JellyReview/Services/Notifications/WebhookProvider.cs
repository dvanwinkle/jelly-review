using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.JellyReview.Services.Notifications;

public class WebhookProvider : INotificationProvider
{
    private readonly Dictionary<string, object> _config;

    public WebhookProvider(Dictionary<string, object> config)
    {
        _config = config;
    }

    private string Url => _config.TryGetValue("url", out var v) ? v.ToString()! : string.Empty;
    private string Method => _config.TryGetValue("method", out var v) ? v.ToString()! : "POST";
    private string? SecretHeader => _config.TryGetValue("secret_header", out var v) ? v.ToString() : null;
    private string? SecretValue => _config.TryGetValue("secret_value", out var v) ? v.ToString() : null;

    private HttpRequestMessage BuildRequest(string method, string url, object body)
    {
        var json = JsonSerializer.Serialize(body);
        var req = new HttpRequestMessage(new HttpMethod(method), url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        if (SecretHeader != null)
            req.Headers.TryAddWithoutValidation(SecretHeader, SecretValue ?? string.Empty);
        return req;
    }

    public async Task<DeliveryResult> TestConnectionAsync()
    {
        try
        {
            using var client = new HttpClient();
            using var req = BuildRequest(Method, Url, new { @event = "test", source = "JellyReview" });
            var resp = await client.SendAsync(req).ConfigureAwait(false);
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
        var body = new
        {
            @event = "pending_review",
            source = "JellyReview",
            media = new
            {
                payload.MediaRecordId, payload.Title, payload.Year, payload.MediaType,
                payload.OfficialRating, payload.Overview, payload.Genres, payload.PendingCount,
                payload.ApproveTokenUrl, payload.DenyTokenUrl, payload.DeferTokenUrl, payload.DetailUrl
            }
        };
        try
        {
            using var client = new HttpClient();
            using var req = BuildRequest(Method, Url, body);
            var resp = await client.SendAsync(req).ConfigureAwait(false);
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
