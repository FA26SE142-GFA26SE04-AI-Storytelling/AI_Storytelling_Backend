using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.Notifications.DTOs;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Notifications.Services;

public class NotificationService : INotificationService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationRealtimePublisher _publisher;

    public NotificationService(IUnitOfWork unitOfWork, INotificationRealtimePublisher publisher)
    {
        _unitOfWork = unitOfWork;
        _publisher = publisher;
    }

    public async Task<NotificationDto> CreateAsync(
        int recipientUserId, NotificationType type, string? payload, CancellationToken cancellationToken = default)
    {
        var notification = new Notification
        {
            RecipientUserId = recipientUserId,
            ChildProfileId = ExtractChildProfileId(payload),
            Type = type,
            Payload = payload,
            Status = NotificationReadStatus.Unread
        };

        await _unitOfWork.Repository<Notification>().AddAsync(notification, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var dto = MapNotification(notification);
        await _publisher.PublishAsync(recipientUserId, dto, cancellationToken);
        return dto;
    }

    public Task<List<NotificationDto>> ListAsync(
        int currentUserId, CancellationToken cancellationToken = default) =>
        ListAsync(currentUserId, null, cancellationToken);

    public async Task<List<NotificationDto>> ListAsync(
        int currentUserId, string? group, CancellationToken cancellationToken)
    {
        if (group != null && !NotificationGroups.IsValid(group))
        {
            throw new BadRequestException("Nhóm thông báo không hợp lệ.");
        }

        var notifications = group == null
            ? await _unitOfWork.Repository<Notification>().FindAsync(
                notification => notification.RecipientUserId == currentUserId,
                cancellationToken: cancellationToken)
            : await FindByTypesAsync(
                currentUserId, NotificationGroups.TypesIn(group), cancellationToken);

        return notifications
            .OrderByDescending(notification => notification.CreatedAt)
            .Select(MapNotification)
            .ToList();
    }

    public Task<int> GetUnreadCountAsync(
        int currentUserId, CancellationToken cancellationToken = default) =>
        _unitOfWork.Repository<Notification>().CountAsync(
            notification => notification.RecipientUserId == currentUserId
                            && notification.Status != NotificationReadStatus.Read,
            cancellationToken);

    public async Task MarkAsReadAsync(
        int notificationId, int currentUserId, CancellationToken cancellationToken = default)
    {
        var repository = _unitOfWork.Repository<Notification>();
        var notification = await repository.GetByIdAsync(notificationId, cancellationToken);
        if (notification == null || notification.RecipientUserId != currentUserId)
        {
            throw new NotFoundException("Thông báo", notificationId);
        }

        if (notification.Status == NotificationReadStatus.Read)
        {
            return;
        }

        var now = DateTime.UtcNow;
        notification.Status = NotificationReadStatus.Read;
        notification.ReadAt = now;
        notification.UpdatedAt = now;
        repository.Update(notification);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkAllAsReadAsync(
        int currentUserId, CancellationToken cancellationToken = default)
    {
        var repository = _unitOfWork.Repository<Notification>();
        var unread = await repository.FindAsync(
            notification => notification.RecipientUserId == currentUserId
                            && notification.Status != NotificationReadStatus.Read,
            cancellationToken: cancellationToken);

        if (unread.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var notification in unread)
        {
            notification.Status = NotificationReadStatus.Read;
            notification.ReadAt = now;
            notification.UpdatedAt = now;
            repository.Update(notification);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(
        int notificationId, int currentUserId, CancellationToken cancellationToken = default)
    {
        var repository = _unitOfWork.Repository<Notification>();
        var notification = await repository.GetByIdAsync(notificationId, cancellationToken);
        if (notification == null || notification.RecipientUserId != currentUserId)
        {
            throw new NotFoundException("Thông báo", notificationId);
        }

        repository.Delete(notification);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static NotificationDto MapNotification(Notification notification) => new()
    {
        Id = notification.Id,
        ChildProfileId = notification.ChildProfileId,
        Type = notification.Type.ToString(),
        Group = NotificationGroups.For(notification.Type),
        Payload = notification.Payload,
        Status = notification.Status.ToString(),
        ReadAt = notification.ReadAt,
        CreatedAt = notification.CreatedAt
    };

    private async Task<IReadOnlyList<Notification>> FindByTypesAsync(
        int currentUserId, IReadOnlyList<NotificationType> types,
        CancellationToken cancellationToken) =>
        await _unitOfWork.Repository<Notification>().FindAsync(
            notification => notification.RecipientUserId == currentUserId
                            && types.Contains(notification.Type),
            cancellationToken: cancellationToken);

    private static int? ExtractChildProfileId(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("childProfileId", out var element)
                && element.ValueKind == JsonValueKind.Number
                && element.TryGetInt32(out var childProfileId))
            {
                return childProfileId;
            }
        }
        catch (JsonException)
        {
            // Payload không hợp lệ không ngăn việc tạo thông báo.
        }

        return null;
    }
}
