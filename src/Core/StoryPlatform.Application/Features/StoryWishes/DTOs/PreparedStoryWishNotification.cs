using StoryPlatform.Application.Features.Notifications.DTOs;

namespace StoryPlatform.Application.Features.StoryWishes;

/// <summary>In-memory delivery data for a committed sweep; not an API response or persisted entity.</summary>
public sealed record PreparedStoryWishNotification(int RecipientUserId, NotificationDto Notification);
