using Moq;
using StoryPlatform.Application.Features.ContentReports.Interfaces;
using StoryPlatform.Application.Features.ContentReports.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ContentReports;

public class CommunitySubmissionArchiveHandlerTests
{
    private readonly ContentReportHarness _h = new();
    private readonly CommunitySubmissionArchiveHandler _sut;

    // Truyện riêng của bé (do phụ huynh ReporterId viết) đã được nộp lên Community bởi OtherParentId.
    private const int Story = ContentReportHarness.PrivateStoryId;
    private const int Author = ContentReportHarness.ReporterId;
    private const int Submitter = ContentReportHarness.OtherParentId;
    private const int Moderator = ContentReportHarness.ModeratorId;
    private const int OtherModerator = ContentReportHarness.OtherModeratorId;

    public CommunitySubmissionArchiveHandlerTests()
    {
        _sut = new CommunitySubmissionArchiveHandler(_h.UnitOfWork.Object, _h.Audit.Object, _h.Notifications.Object);
    }

    private StoryArchiveContext Context(ArchivedReason reason = ArchivedReason.SafetyConcern, int? reporter = null)
    {
        var story = _h.Stories.Single(existing => existing.Id == Story);
        story.Status = StoryStatus.Rejected;
        return new StoryArchiveContext(story, reason, Moderator, DateTime.UtcNow, reporter);
    }

    private ModerationItem Submission(
        int id = 80, ModerationItemStatus status = ModerationItemStatus.Approved, int storyId = Story,
        int submitterId = Submitter)
    {
        var item = new ModerationItem
        {
            Id = id, ItemType = ModerationItemType.CommunitySubmission, Status = status, StoryId = storyId,
            StoryVersionId = 60, CreatedByUserId = submitterId
        };
        if (status == ModerationItemStatus.Claimed)
        {
            item.ClaimedByUserId = OtherModerator;
            item.ClaimedAt = DateTime.UtcNow.AddMinutes(-10);
        }

        _h.Items.Add(item);
        return item;
    }

    private void VerifyNotified(int recipient, NotificationType type, Times times) =>
        _h.Notifications.Verify(n => n.CreateAsync(
            recipient, type, It.IsAny<string?>(), It.IsAny<CancellationToken>()), times);

    // ---- Approved → Removed ----

    [Theory]
    [InlineData(ArchivedReason.SafetyConcern)]
    [InlineData(ArchivedReason.Copyright)]
    [InlineData(ArchivedReason.Inappropriate)]
    public async Task HandleAsync_ApprovedSubmission_IsRemoved(ArchivedReason reason)
    {
        var item = Submission();
        var before = DateTime.UtcNow;

        await _sut.HandleAsync(Context(reason));

        Assert.Equal(ModerationItemStatus.Removed, item.Status);
        Assert.True(item.RemovedAt >= before);
        Assert.Equal("story_archived", item.ClosedReason);
        Assert.Equal(1, item.RowVersion);
    }

    [Fact]
    public async Task HandleAsync_SupervisorPreference_KeepsTheLibraryCopy()
    {
        var item = Submission();

        await _sut.HandleAsync(Context(ArchivedReason.SupervisorPreference));

        Assert.Equal(ModerationItemStatus.Approved, item.Status);
        Assert.Null(item.RemovedAt);
        _h.Notifications.Verify(n => n.CreateAsync(
            It.IsAny<int>(), It.IsAny<NotificationType>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _h.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- Pending → Withdrawn, Claimed → Closed ----

    [Fact]
    public async Task HandleAsync_PendingSubmission_IsWithdrawn()
    {
        var item = Submission(status: ModerationItemStatus.Pending);
        var before = DateTime.UtcNow;

        await _sut.HandleAsync(Context());

        Assert.Equal(ModerationItemStatus.Withdrawn, item.Status);
        Assert.True(item.WithdrawnAt >= before);
        Assert.Equal("story_archived", item.ClosedReason);
        Assert.Null(item.RemovedAt);
        Assert.Equal(1, item.RowVersion);
    }

    [Fact]
    public async Task HandleAsync_ClaimedSubmission_IsClosedAndTheModeratorIsNotified()
    {
        var item = Submission(status: ModerationItemStatus.Claimed);
        var before = DateTime.UtcNow;

        await _sut.HandleAsync(Context());

        Assert.Equal(ModerationItemStatus.Closed, item.Status);
        Assert.True(item.DecidedAt >= before);
        Assert.Equal("story_archived", item.ClosedReason);
        Assert.Equal(OtherModerator, item.ClaimedByUserId);
        Assert.Null(item.DecidedByUserId);
        _h.Notifications.Verify(n => n.CreateAsync(
            OtherModerator, NotificationType.ContentModerationResult,
            It.Is<string?>(payload => payload!.Contains("\"decision\":\"closed\"")
                                      && payload.Contains("CommunitySubmission")
                                      && payload.Contains("\"itemId\":80")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_ClaimantNoLongerActive_StillClosesButSkipsThatNotification()
    {
        var item = Submission(status: ModerationItemStatus.Claimed);
        _h.Users.Single(user => user.Id == OtherModerator).Status = AccountStatus.Suspended;

        await _sut.HandleAsync(Context());

        Assert.Equal(ModerationItemStatus.Closed, item.Status);
        VerifyNotified(OtherModerator, NotificationType.ContentModerationResult, Times.Never());
    }

    [Theory]
    [InlineData(ModerationItemStatus.Rejected)]
    [InlineData(ModerationItemStatus.Withdrawn)]
    [InlineData(ModerationItemStatus.Removed)]
    [InlineData(ModerationItemStatus.Closed)]
    public async Task HandleAsync_AlreadyFinishedSubmissions_AreLeftAlone(ModerationItemStatus status)
    {
        var item = Submission(status: status);

        await _sut.HandleAsync(Context());

        Assert.Equal(status, item.Status);
        Assert.Equal(0, item.RowVersion);
    }

    // ---- thông báo cho người nộp (đúng một thông báo, không lộ ai báo cáo) ----

    [Fact]
    public async Task HandleAsync_NotifiesTheSubmitterOnceWithTheGeneralReasonOnly()
    {
        Submission();

        await _sut.HandleAsync(Context(ArchivedReason.Copyright, reporter: Author));

        _h.Notifications.Verify(n => n.CreateAsync(
            Submitter, NotificationType.StoryArchivedByModeration,
            It.Is<string?>(payload => payload!.Contains("Copyright")
                                      && payload.Contains($"\"storyId\":{Story}")
                                      && !payload.Contains("moderator", StringComparison.OrdinalIgnoreCase)
                                      && !payload.Contains("reporter", StringComparison.OrdinalIgnoreCase)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(ModerationItemStatus.Pending)]
    [InlineData(ModerationItemStatus.Claimed)]
    [InlineData(ModerationItemStatus.Approved)]
    public async Task HandleAsync_TheSubmitterIsToldForEveryAffectedSubmission(ModerationItemStatus status)
    {
        Submission(status: status);

        await _sut.HandleAsync(Context());

        VerifyNotified(Submitter, NotificationType.StoryArchivedByModeration, Times.Once());
    }

    [Fact]
    public async Task HandleAsync_SubmitterIsTheAuthor_IsNotNotifiedAgain()
    {
        Submission(submitterId: Author);

        await _sut.HandleAsync(Context());

        VerifyNotified(Author, NotificationType.StoryArchivedByModeration, Times.Never());
    }

    [Fact]
    public async Task HandleAsync_SubmitterIsTheReporter_IsNotNotifiedAsOwner()
    {
        Submission();

        await _sut.HandleAsync(Context(reporter: Submitter));

        VerifyNotified(Submitter, NotificationType.StoryArchivedByModeration, Times.Never());
    }

    [Fact]
    public async Task HandleAsync_SubmitterNoLongerActive_IsSkipped()
    {
        Submission();
        _h.Users.Single(user => user.Id == Submitter).Status = AccountStatus.Suspended;

        await _sut.HandleAsync(Context());

        VerifyNotified(Submitter, NotificationType.StoryArchivedByModeration, Times.Never());
    }

    [Fact]
    public async Task HandleAsync_TwoAffectedSubmissionsOfTheSameSubmitter_GetOneNotification()
    {
        Submission(80, ModerationItemStatus.Claimed);
        Submission(81, ModerationItemStatus.Approved);

        await _sut.HandleAsync(Context());

        VerifyNotified(Submitter, NotificationType.StoryArchivedByModeration, Times.Once());
    }

    // ---- phạm vi ----

    [Fact]
    public async Task HandleAsync_OnlyCommunityItemsOfThatStory_AreTouched()
    {
        var otherStory = Submission(80, storyId: ContentReportHarness.PublicStoryId);
        var publicReview = new ModerationItem
        {
            Id = 90, ItemType = ModerationItemType.PublicStoryReview, Status = ModerationItemStatus.Pending,
            StoryId = Story
        };
        _h.Items.Add(publicReview);
        var mine = Submission(81);

        await _sut.HandleAsync(Context());

        Assert.Equal(ModerationItemStatus.Approved, otherStory.Status);
        Assert.Equal(ModerationItemStatus.Pending, publicReview.Status);
        Assert.Equal(ModerationItemStatus.Removed, mine.Status);
    }

    [Fact]
    public async Task HandleAsync_WritesAnAuditEntryPerAffectedItem()
    {
        Submission(80);
        Submission(81, ModerationItemStatus.Pending);

        await _sut.HandleAsync(Context());

        foreach (var id in new[] { 80, 81 })
        {
            _h.Audit.Verify(a => a.LogAsync(
                Moderator, "CommunitySubmissionClosedByArchive", nameof(ModerationItem), id,
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Fact]
    public async Task HandleAsync_NoSubmission_DoesNothing()
    {
        await _sut.HandleAsync(Context());

        _h.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _h.Audit.Verify(a => a.LogAsync(
            It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
