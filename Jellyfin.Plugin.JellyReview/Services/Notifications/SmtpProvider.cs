using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace Jellyfin.Plugin.JellyReview.Services.Notifications;

public class SmtpProvider : INotificationProvider
{
    private readonly Dictionary<string, object> _config;

    public SmtpProvider(Dictionary<string, object> config)
    {
        _config = config;
    }

    private string Host => _config.TryGetValue("host", out var v) ? v.ToString()! : string.Empty;
    private int Port => _config.TryGetValue("port", out var v) && int.TryParse(v.ToString(), out var p) ? p : 587;
    private string Username => _config.TryGetValue("username", out var v) ? v.ToString()! : string.Empty;
    private string Password => _config.TryGetValue("password", out var v) ? v.ToString()! : string.Empty;
    private string FromAddress => _config.TryGetValue("from_address", out var v) ? v.ToString()! : string.Empty;
    private string ToAddress => _config.TryGetValue("to_address", out var v) ? v.ToString()! : string.Empty;
    private bool UseTls => !_config.TryGetValue("use_tls", out var v) || v is not false;

    private string BuildHtml(ReviewNotificationPayload payload)
    {
        var rating = HttpUtility.HtmlEncode(payload.OfficialRating ?? "NR");
        var title = HttpUtility.HtmlEncode(payload.Title);
        var overview = HttpUtility.HtmlEncode((payload.Overview ?? string.Empty)[..Math.Min(300, payload.Overview?.Length ?? 0)]);
        var genres = HttpUtility.HtmlEncode(string.Join(", ", payload.Genres));
        return $"""
            <html><body style="font-family:sans-serif;max-width:600px;margin:auto">
              <h2>Review Needed: {title}</h2>
              <p><strong>Rating:</strong> {rating} &nbsp;|&nbsp; <strong>Type:</strong> {payload.MediaType}</p>
              <p><strong>Genres:</strong> {genres}</p>
              <p>{overview}</p>
              <hr>
              <p>
                <a href="{payload.ApproveTokenUrl}" style="background:#22c55e;color:white;padding:8px 16px;border-radius:4px;text-decoration:none;margin-right:8px">Approve</a>
                <a href="{payload.DenyTokenUrl}" style="background:#ef4444;color:white;padding:8px 16px;border-radius:4px;text-decoration:none;margin-right:8px">Deny</a>
                <a href="{payload.DeferTokenUrl}" style="background:#f59e0b;color:white;padding:8px 16px;border-radius:4px;text-decoration:none;margin-right:8px">Defer</a>
                <a href="{payload.DetailUrl}" style="background:#6366f1;color:white;padding:8px 16px;border-radius:4px;text-decoration:none">Details</a>
              </p>
            </body></html>
            """;
    }

    private void Send(string subject, string htmlBody)
    {
        using var msg = new MailMessage(FromAddress, ToAddress, subject, htmlBody) { IsBodyHtml = true };
        using var smtp = new SmtpClient(Host, Port)
        {
            Credentials = new NetworkCredential(Username, Password),
            EnableSsl = UseTls
        };
        smtp.Send(msg);
    }

    public Task<DeliveryResult> TestConnectionAsync()
    {
        try
        {
            Send("JellyReview test", "<p>Connection test successful!</p>");
            return Task.FromResult(new DeliveryResult { Success = true });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new DeliveryResult { Success = false, Error = ex.Message });
        }
    }

    public Task<DeliveryResult> SendReviewNotificationAsync(ReviewNotificationPayload payload)
    {
        try
        {
            Send($"Review Needed: {payload.Title}", BuildHtml(payload));
            return Task.FromResult(new DeliveryResult { Success = true });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new DeliveryResult { Success = false, Error = ex.Message });
        }
    }

    public Task<DeliveryResult> SendActionConfirmationAsync(string title, string action, string? actor = null)
        => Task.FromResult(new DeliveryResult { Success = true });
}
