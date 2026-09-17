namespace StoryPlatform.Domain.Enums;

public enum NotificationType
{
    SupervisionInvite = 1,
    SupervisionRevokedCascade = 2,
    StoryShared = 3,
    HoldModeAlert = 4,
    TeacherInteractionScore = 5,
    AssignmentCancelled = 6,
    RecommendationAwaitingReview = 7,
    OrgTeacherInvite = 8,
    OrgConsentRequest = 9,
    OrgUnenrollmentNotice = 10,
    DataRequestSlaWarning = 11,
    AdminRoleChanged = 12,
    ContentReported = 13,
    PaymentConfirmed = 14,
    PermissionRequestCreated = 15,
    PermissionRequestAccepted = 16,
    PermissionRequestRejected = 17,
    OwnershipTransferRequested = 18,
    OwnershipTransferAccepted = 19,
    OwnershipTransferRejected = 20
}
