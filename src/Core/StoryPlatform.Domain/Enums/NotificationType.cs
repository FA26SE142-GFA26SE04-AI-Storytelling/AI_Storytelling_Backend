namespace StoryPlatform.Domain.Enums;

public enum NotificationType
{
    SupervisionInvite = 1,
    SupervisionRevokedCascade = 2,
    StoryShared = 3,
    HoldModeAlert = 4,
    AssignmentCancelled = 6,
    RecommendationAwaitingReview = 7,
    DataRequestSlaWarning = 11,
    AdminRoleChanged = 12,
    ContentReported = 13,
    PaymentConfirmed = 14,
    PermissionRequestCreated = 15,
    PermissionRequestAccepted = 16,
    PermissionRequestRejected = 17,
    OwnershipTransferRequested = 18,
    OwnershipTransferAccepted = 19,
    OwnershipTransferRejected = 20,
    ContentReportSlaWarning = 21,
    ContentReportResolved = 22,
    SupervisionAccepted = 23,
    SupervisionRejected = 24
}
