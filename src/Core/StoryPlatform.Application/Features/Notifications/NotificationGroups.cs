using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Notifications;

/// <summary>Nhóm hiển thị của Trung tâm thông báo.</summary>
public static class NotificationGroups
{
    public const string Stories = "stories";
    public const string Children = "children";
    public const string Supervisors = "supervisors";
    public const string Account = "account";
    public const string Moderation = "moderation";

    private static readonly string[] All =
        [Stories, Children, Supervisors, Account, Moderation];

    public static string For(NotificationType type) => type switch
    {
        NotificationType.SupervisionInvite
            or NotificationType.SupervisionInviteResult
            or NotificationType.SupervisionRevoked
            or NotificationType.PermissionRequest
            or NotificationType.PermissionRequestResult
            or NotificationType.OwnershipTransferRequest
            or NotificationType.OwnershipTransferResult => Supervisors,

        NotificationType.HoldModeAlert
            or NotificationType.RecommendationAwaitingReview
            or NotificationType.AssignmentCancelled
            or NotificationType.RoadmapStoryAdded
            or NotificationType.RoadmapStoryRemoved
            or NotificationType.RoadmapCompleted => Children,

        NotificationType.ContentReported
            or NotificationType.ModerationSlaWarning
            or NotificationType.DataRequestSlaWarning
            or NotificationType.ContentModerationResult => Moderation,

        NotificationType.ContentReportResolved
            or NotificationType.StoryShared => Stories,

        _ => Account
    };

    public static bool IsValid(string? group) =>
        group != null && All.Contains(group);

    public static IReadOnlyList<NotificationType> TypesIn(string group) =>
        Enum.GetValues<NotificationType>()
            .Where(type => For(type) == group)
            .ToList();
}
