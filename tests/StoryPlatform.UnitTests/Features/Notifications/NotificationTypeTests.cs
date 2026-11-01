using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Notifications;

public class NotificationTypeTests
{
    [Theory]
    [InlineData("SupervisionInvite")]
    [InlineData("SupervisionInviteResult")]
    [InlineData("SupervisionRevoked")]
    [InlineData("PermissionRequest")]
    [InlineData("PermissionRequestResult")]
    [InlineData("OwnershipTransferRequest")]
    [InlineData("OwnershipTransferResult")]
    [InlineData("AccountSecurityChanged")]
    [InlineData("ModerationSlaWarning")]
    public void NotificationType_ContainsSpecName(string name) =>
        Assert.Contains(name, Enum.GetNames<NotificationType>());

    [Theory]
    [InlineData("SupervisionAccepted")]
    [InlineData("SupervisionRejected")]
    [InlineData("SupervisionRevokedCascade")]
    [InlineData("PermissionRequestCreated")]
    [InlineData("PermissionRequestAccepted")]
    [InlineData("PermissionRequestRejected")]
    [InlineData("OwnershipTransferRequested")]
    [InlineData("OwnershipTransferAccepted")]
    [InlineData("OwnershipTransferRejected")]
    [InlineData("ContentReportSlaWarning")]
    public void NotificationType_DoesNotContainOldName(string name) =>
        Assert.DoesNotContain(name, Enum.GetNames<NotificationType>());
}
