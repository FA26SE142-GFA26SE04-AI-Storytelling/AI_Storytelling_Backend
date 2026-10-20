using StoryPlatform.Application.Features.Notifications;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Notifications;

public class NotificationGroupsTests
{
    [Theory]
    [InlineData(NotificationType.SupervisionInvite, "supervisors")]
    [InlineData(NotificationType.SupervisionInviteResult, "supervisors")]
    [InlineData(NotificationType.SupervisionRevoked, "supervisors")]
    [InlineData(NotificationType.PermissionRequest, "supervisors")]
    [InlineData(NotificationType.PermissionRequestResult, "supervisors")]
    [InlineData(NotificationType.OwnershipTransferRequest, "supervisors")]
    [InlineData(NotificationType.OwnershipTransferResult, "supervisors")]
    [InlineData(NotificationType.HoldModeAlert, "children")]
    [InlineData(NotificationType.RecommendationAwaitingReview, "children")]
    [InlineData(NotificationType.RoadmapStoryAdded, "children")]
    [InlineData(NotificationType.RoadmapStoryRemoved, "children")]
    [InlineData(NotificationType.RoadmapCompleted, "children")]
    [InlineData(NotificationType.AccountSecurityChanged, "account")]
    [InlineData(NotificationType.PaymentConfirmed, "account")]
    [InlineData(NotificationType.DataRequestCompleted, "account")]
    [InlineData(NotificationType.AdminRoleChanged, "account")]
    [InlineData(NotificationType.ContentReported, "moderation")]
    [InlineData(NotificationType.ModerationSlaWarning, "moderation")]
    [InlineData(NotificationType.DataRequestSlaWarning, "moderation")]
    [InlineData(NotificationType.ContentModerationResult, "moderation")]
    [InlineData(NotificationType.ContentReportResolved, "stories")]
    public void For_ReturnsSpecGroup(NotificationType type, string expected) =>
        Assert.Equal(expected, NotificationGroups.For(type));

    [Fact]
    public void EveryNotificationType_BelongsToAValidGroup()
    {
        foreach (var type in Enum.GetValues<NotificationType>())
        {
            Assert.True(NotificationGroups.IsValid(NotificationGroups.For(type)), type.ToString());
        }
    }

    [Fact]
    public void TypesIn_ReturnsOnlyTypesOfThatGroup() =>
        Assert.All(NotificationGroups.TypesIn(NotificationGroups.Supervisors),
            type => Assert.Equal(NotificationGroups.Supervisors, NotificationGroups.For(type)));

    [Theory]
    [InlineData("stories", true)]
    [InlineData("children", true)]
    [InlineData("supervisors", true)]
    [InlineData("account", true)]
    [InlineData("moderation", true)]
    [InlineData("other", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValid_AcceptsOnlyKnownGroups(string? group, bool expected) =>
        Assert.Equal(expected, NotificationGroups.IsValid(group));
}
