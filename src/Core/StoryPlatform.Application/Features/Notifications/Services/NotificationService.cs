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

    public async Task<List<NotificationDto>> ListAsync(
        int currentUserId, CancellationToken cancellationToken = default)
    {
        var notifications = await _unitOfWork.Repository<Notification>().FindAsync(
            notification => notification.RecipientUserId == currentUserId,
            cancellationToken: cancellationToken);

        return notifications
            .OrderByDescending(notification => notification.CreatedAt)
            .Select(MapNotification)
            .ToList();
    }

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
        Type = notification.Type.ToString(),
        Payload = notification.Payload,
        Status = notification.Status.ToString(),
        ReadAt = notification.ReadAt,
        CreatedAt = notification.CreatedAt
    };
}
