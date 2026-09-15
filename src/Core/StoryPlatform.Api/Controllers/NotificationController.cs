using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.Notifications.DTOs;
using StoryPlatform.Application.Features.Notifications.Interfaces;

namespace StoryPlatform.Api.Controllers;

/// <summary>
/// Quản lý thông báo của người dùng hiện tại.
/// </summary>
[Authorize]
public class NotificationController : BaseApiController
{
    private readonly INotificationService _notificationService;

    public NotificationController(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    /// <summary>
    /// Lấy danh sách thông báo của người dùng hiện tại, mới nhất trước.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<NotificationDto>>>> ListNotifications(
        CancellationToken cancellationToken)
    {
        var result = await _notificationService.ListAsync(GetCurrentUserId(), cancellationToken);
        return HandleResult(result, "Lấy danh sách thông báo thành công.");
    }

    /// <summary>
    /// Đánh dấu một thông báo là đã đọc.
    /// </summary>
    [HttpPut("{notificationId:int}/read")]
    public async Task<ActionResult<ApiResponse<object?>>> MarkAsRead(
        int notificationId, CancellationToken cancellationToken)
    {
        await _notificationService.MarkAsReadAsync(notificationId, GetCurrentUserId(), cancellationToken);
        return HandleResult<object?>(null, "Đánh dấu đã đọc thành công.");
    }

    /// <summary>
    /// Đánh dấu toàn bộ thông báo chưa đọc của người dùng hiện tại là đã đọc.
    /// </summary>
    [HttpPut("read-all")]
    public async Task<ActionResult<ApiResponse<object?>>> MarkAllAsRead(
        CancellationToken cancellationToken)
    {
        await _notificationService.MarkAllAsReadAsync(GetCurrentUserId(), cancellationToken);
        return HandleResult<object?>(null, "Đánh dấu tất cả thông báo đã đọc thành công.");
    }

    /// <summary>
    /// Xoá một thông báo của người dùng hiện tại.
    /// </summary>
    [HttpDelete("{notificationId:int}")]
    public async Task<ActionResult<ApiResponse<object?>>> DeleteNotification(
        int notificationId, CancellationToken cancellationToken)
    {
        await _notificationService.DeleteAsync(notificationId, GetCurrentUserId(), cancellationToken);
        return HandleResult<object?>(null, "Xoá thông báo thành công.");
    }
}
