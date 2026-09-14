using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace StoryPlatform.Api.Hubs;

/// <summary>
/// Hub đẩy thông báo theo thời gian thực tới người dùng đã xác thực.
/// </summary>
[Authorize]
public class NotificationHub : Hub
{
}
