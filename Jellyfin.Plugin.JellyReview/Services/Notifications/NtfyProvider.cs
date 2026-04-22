using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.JellyReview.Services.Notifications;

public class NtfyProvider : INotificationProvider
{
    private readonly Dictionary<string, object> _config;

    public NtfyProvider(Dictionary<string, object> config)
    {
        _config = config;
    }

    private string ServerUrl => _config.TryGetValue("server_url", out var v) ? v.ToString()!.TrimEnd('/') : "https://ntfy.sh";
    private string Topic => _config.TryGetValue("topic", out var v) ? v.ToString()! : string.Empty;
    private string? Token => _config.TryGetValue("token", out var v) ? v.ToString() : null;

    private HttpClient BuildClient()
    {
        var client = new HttpClient();
        if (!string.IsNullOrEmpty(Token))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        return client;
    }

    public async Task<DeliveryResult> TestConnectionAsync()
    {
        try
        {
            using var client = BuildClient();
            client.DefaultRequestHeaders.Add("Title", "JellyReview test");
            var resp = await client.PostAsync($"{ServerUrl}/{Topic}",
                new StringContent("Connection test successful", Encoding.UTF8)).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            var json = JsonSerializer.Deserialize<JsonElement>(body);
            return new DeliveryResult { Success = true, ProviderMessageId = json.TryGetProperty("id", out var id) ? id.GetString() : null };
        }
        catch (Exception ex)
        {
            return new DeliveryResult { Success = false, Error = ex.Message };
        }
    }

    public async Task<DeliveryResult> SendReviewNotificationAsync(ReviewNotificationPayload payload)
    {
        var rating = payload.OfficialRating ?? "NR";
        var message = payload.Overview ?? "No overview available";
        if (message.Length > 200) message = message[..200];
        var actions = $"http, Approve, {payload.ApproveTokenUrl}, method=POST, clear=true; " +
                      $"http, Deny, {payload.DenyTokenUrl}, method=POST, clear=true; " +
                      $"view, Details, {payload.DetailUrl}";

        try
        {
            using var client = BuildClient();
            client.DefaultRequestHeaders.Add("Title", $"Review: {payload.Title} ({rating})");
            client.DefaultRequestHeaders.Add("Tags", "movie_camera,bell");
            client.DefaultRequestHeaders.Add("Actions", actions);
            client.DefaultRequestHeaders.Add("Attach", payload.PosterProxyUrl);

            var resp = await client.PostAsync($"{ServerUrl}/{Topic}",
                new StringContent(message, Encoding.UTF8)).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            var json = JsonSerializer.Deserialize<JsonElement>(body);
            return new DeliveryResult { Success = true, ProviderMessageId = json.TryGetProperty("id", out var id) ? id.GetString() : null };
        }
        catch (Exception ex)
        {
            return new DeliveryResult { Success = false, Error = ex.Message };
        }
    }

    public Task<DeliveryResult> SendActionConfirmationAsync(string title, string action, string? actor = null)
        => Task.FromResult(new DeliveryResult { Success = true });
}
