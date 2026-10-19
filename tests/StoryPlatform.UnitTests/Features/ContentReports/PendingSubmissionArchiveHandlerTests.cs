using Moq;
using StoryPlatform.Application.Features.ContentReports.Interfaces;
using StoryPlatform.Application.Features.ContentReports.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ContentReports;

public class PendingSubmissionArchiveHandlerTests
{
    private readonly ContentReportHarness _h = new();
    private readonly PendingSubmissionArchiveHandler _sut;

    public PendingSubmissionArchiveHandlerTests()
    {
        _sut = new PendingSubmissionArchiveHandler(
            _h.UnitOfWork.Object, _h.Audit.Object, _h.Notifications.Object);
    }

    private StoryArchiveContext Context(ArchivedReason reason = ArchivedReason.Copyright)
    {
        var story = _h.Stories.Single(existing => existing.Id == ContentReportHarness.PublicStoryId);
        story.Status = StoryStatus.Archived;
        return new StoryArchiveContext(
            story, reason, ContentReportHarness.ModeratorId, DateTime.UtcNow);
    }

    private ModerationItem Submitted(ModerationItemStatus status)
    {
        var version = _h.Versions.Single(existing => existing.Id == 51);
        version.ReviewStatus = VersionReviewStatus.Pending;
        version.ReviewSubmittedAt = DateTime.UtcNow.AddHours(-1);
        var item = new ModerationItem
        {
            Id = 40,
            ItemType = ModerationItemType.PublicStoryReview,
            Status = status,
            StoryId = ContentReportHarness.PublicStoryId,
            StoryVersionId = version.Id,
            CreatedByUserId = ContentReportHarness.CreatorId
        };
        if (status == ModerationItemStatus.Claimed)
        {
            item.ClaimedByUserId = ContentReportHarness.OtherModeratorId;
            item.ClaimedAt = DateTime.UtcNow.AddMinutes(-10);
        }

        _h.Items.Add(item);
        return item;
    }

    [Theory]
    [InlineData(ArchivedReason.SafetyConcern)]
    [InlineData(ArchivedReason.Copyright)]
    [InlineData(ArchivedReason.Inappropriate)]
    public async Task PendingSubmission_IsWithdrawnAndVersionReturnsToNotSubmitted(
        ArchivedReason reason)
    {
        var item = Submitted(ModerationItemStatus.Pending);

        await _sut.HandleAsync(Context(reason));

        Assert.Equal(ModerationItemStatus.Withdrawn, item.Status);
        Assert.NotNull(item.WithdrawnAt);
        Assert.Equal(1, item.RowVersion);
        var version = _h.Versions.Single(existing => existing.Id == 51);
        Assert.Equal(VersionReviewStatus.None, version.ReviewStatus);
        Assert.Null(version.ReviewSubmittedAt);
        _h.Notifications.Verify(notification => notification.CreateAsync(
            It.IsAny<int>(), It.IsAny<NotificationType>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ClaimedSubmission_IsClosedAndActiveClaimantIsNotified()
    {
        var item = Submitted(ModerationItemStatus.Claimed);

        await _sut.HandleAsync(Context());

        Assert.Equal(ModerationItemStatus.Closed, item.Status);
        Assert.Equal(PendingSubmissionArchiveHandler.ClosedReason, item.ClosedReason);
        Assert.Null(item.DecisionReason);
        Assert.Equal(ContentReportHarness.OtherModeratorId, item.ClaimedByUserId);
        Assert.NotNull(item.DecidedAt);
        Assert.Equal(1, item.RowVersion);
        _h.Notifications.Verify(notification => notification.CreateAsync(
            ContentReportHarness.OtherModeratorId,
            NotificationType.ContentModerationResult,
            It.Is<string?>(payload => payload != null
                                      && payload.Contains("\"decision\":\"closed\"")
                                      && payload.Contains("story_archived")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InactiveClaimant_IsNotNotifiedButItemStillCloses()
    {
        var item = Submitted(ModerationItemStatus.Claimed);
        _h.Users.Single(user => user.Id == ContentReportHarness.OtherModeratorId).Status =
            AccountStatus.Suspended;

        await _sut.HandleAsync(Context());

        Assert.Equal(ModerationItemStatus.Closed, item.Status);
        _h.Notifications.Verify(notification => notification.CreateAsync(
            It.IsAny<int>(), It.IsAny<NotificationType>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(ModerationItemStatus.Approved)]
    [InlineData(ModerationItemStatus.Rejected)]
    [InlineData(ModerationItemStatus.Withdrawn)]
    [InlineData(ModerationItemStatus.Closed)]
    public async Task FinishedSubmission_IsLeftAlone(ModerationItemStatus status)
    {
        var item = Submitted(status);

        await _sut.HandleAsync(Context());

        Assert.Equal(status, item.Status);
        Assert.Equal(0, item.RowVersion);
    }

    [Fact]
    public async Task OtherStoryAndOtherItemTypes_AreLeftAlone()
    {
        var otherStory = Submitted(ModerationItemStatus.Pending);
        otherStory.StoryId = ContentReportHarness.PrivateStoryId;
        var report = _h.AddReportItem(ContentReportReason.SafetyConcern);

        await _sut.HandleAsync(Context());

        Assert.Equal(ModerationItemStatus.Pending, otherStory.Status);
        Assert.Equal(ModerationItemStatus.Claimed, report.Status);
        _h.UnitOfWork.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
