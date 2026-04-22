using System.Collections.Generic;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.JellyReview.Services.Notifications;

public interface INotificationProvider
{
    Task<DeliveryResult> TestConnectionAsync();
    Task<DeliveryResult> SendReviewNotificationAsync(ReviewNotificationPayload payload);
    Task<DeliveryResult> SendActionConfirmationAsync(string title, string action, string? actor = null);
}
