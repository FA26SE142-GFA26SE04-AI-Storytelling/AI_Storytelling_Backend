using Microsoft.AspNetCore.SignalR;
using StoryPlatform.Api.Hubs;
using StoryPlatform.Application.Features.Notifications.DTOs;
using StoryPlatform.Application.Features.Notifications.Interfaces;

namespace StoryPlatform.Api.Realtime;

public class SignalRNotificationPublisher : INotificationRealtimePublisher
{
    private readonly IHubContext<NotificationHub> _hubContext;

    public SignalRNotificationPublisher(IHubContext<NotificationHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task PublishAsync(
        int recipientUserId, NotificationDto notification, CancellationToken cancellationToken = default)
    {
        return _hubContext.Clients.User(recipientUserId.ToString())
            .SendAsync("ReceiveNotification", notification, cancellationToken);
    }
}
