using Moq;
using StoryPlatform.Application.Features.Moderation.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.UnitTests.Features.RoadmapTemplates;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Moderation;

public class ModerationSweepRoadmapTests
{
    private readonly RoadmapHarness _h = new();
    private readonly ModerationSweepService _sut;

    private const int Departing = RoadmapHarness.ModeratorId;
    private const int Colleague = RoadmapHarness.OtherModeratorId;

    public ModerationSweepRoadmapTests()
    {
        _sut = new ModerationSweepService(_h.UnitOfWork.Object, _h.Notifications.Object);
    }

    private void Suspend(int userId) =>
        _h.Users.Single(user => user.Id == userId).Status = AccountStatus.Suspended;

    private (ReadingRoadmap Roadmap, ModerationItem Item) PendingReview(
        int roadmapId, int itemId, int createdBy = Departing)
    {
        var roadmap = _h.AddRoadmap(
            roadmapId, AgeBand.Age_6_8, 5 + roadmapId,
            RoadmapTemplateStatus.PendingReview, createdBy, 10);
        var item = new ModerationItem
        {
            Id = itemId,
            ItemType = ModerationItemType.RoadmapTemplateReview,
            Status = ModerationItemStatus.Pending,
            ReadingRoadmapId = roadmapId,
            CreatedByUserId = createdBy
        };
        _h.Items.Add(item);
        return (roadmap, item);
    }

    [Fact]
    public async Task Withdraw_InactiveCreatorsPendingReview_ReturnsRoadmapToDraft()
    {
        var (roadmap, item) = PendingReview(1, 40);
        Suspend(Departing);

        var count = await _sut.WithdrawRoadmapReviewsOfInactiveCreatorsAsync();

        Assert.Equal(1, count);
        Assert.Equal(ModerationItemStatus.Withdrawn, item.Status);
        Assert.NotNull(item.WithdrawnAt);
        Assert.Equal(1, item.RowVersion);
        Assert.Equal(RoadmapTemplateStatus.Draft, roadmap.Status);
        Assert.Null(roadmap.SubmittedAt);
        _h.UnitOfWork.Verify(unit => unit.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Withdraw_LeavesActiveCreatorAndClaimedItemAlone()
    {
        var active = PendingReview(1, 40, Colleague);
        var claimed = PendingReview(2, 41);
        claimed.Item.Status = ModerationItemStatus.Claimed;
        claimed.Item.ClaimedByUserId = Colleague;
        claimed.Item.ClaimedAt = DateTime.UtcNow;
        Suspend(Departing);

        var count = await _sut.WithdrawRoadmapReviewsOfInactiveCreatorsAsync();

        Assert.Equal(0, count);
        Assert.Equal(ModerationItemStatus.Pending, active.Item.Status);
        Assert.Equal(RoadmapTemplateStatus.PendingReview, active.Roadmap.Status);
        Assert.Equal(ModerationItemStatus.Claimed, claimed.Item.Status);
        Assert.Equal(RoadmapTemplateStatus.PendingReview, claimed.Roadmap.Status);
    }

    [Fact]
    public async Task Withdraw_ExpiredClaimReturnedToQueue_IsWithdrawnOnNextSweep()
    {
        var (roadmap, item) = PendingReview(1, 40);
        item.Status = ModerationItemStatus.Claimed;
        item.ClaimedByUserId = Colleague;
        item.ClaimedAt = DateTime.UtcNow.AddHours(-3);
        Suspend(Departing);

        await _sut.ReleaseExpiredClaimsAsync();
        var count = await _sut.WithdrawRoadmapReviewsOfInactiveCreatorsAsync();

        Assert.Equal(1, count);
        Assert.Equal(ModerationItemStatus.Withdrawn, item.Status);
        Assert.Equal(RoadmapTemplateStatus.Draft, roadmap.Status);
    }

    [Fact]
    public async Task Withdraw_CreatorFilterOnlyAffectsRequestedModerator()
    {
        _h.AddUser(13, "moderator_demo3", UserRole.Moderator);
        var mine = PendingReview(1, 40);
        var theirs = PendingReview(2, 41, 13);
        Suspend(Departing);
        Suspend(13);

        var count = await _sut.WithdrawRoadmapReviewsOfInactiveCreatorsAsync(Departing);

        Assert.Equal(1, count);
        Assert.Equal(ModerationItemStatus.Withdrawn, mine.Item.Status);
        Assert.Equal(ModerationItemStatus.Pending, theirs.Item.Status);
    }

    [Fact]
    public async Task Withdraw_DeletedCreatorCountsAsInactive_AndIsIdempotent()
    {
        var (_, item) = PendingReview(1, 40);
        _h.Users.Single(user => user.Id == Departing).IsDeleted = true;

        Assert.Equal(1, await _sut.WithdrawRoadmapReviewsOfInactiveCreatorsAsync());
        Assert.Equal(0, await _sut.WithdrawRoadmapReviewsOfInactiveCreatorsAsync());
        Assert.Equal(ModerationItemStatus.Withdrawn, item.Status);
    }

    [Fact]
    public async Task Withdraw_DoesNotTouchDraftPublishedOrOtherItemTypes()
    {
        var draft = _h.AddRoadmap(3, AgeBand.Age_6_8, 6, RoadmapTemplateStatus.Draft, Departing, 10);
        var published = _h.AddRoadmap(4, AgeBand.Age_9_10, 6, RoadmapTemplateStatus.Published, Departing, 12);
        var other = new ModerationItem
        {
            Id = 50,
            ItemType = ModerationItemType.PublicStoryReview,
            Status = ModerationItemStatus.Pending,
            CreatedByUserId = Departing
        };
        _h.Items.Add(other);
        Suspend(Departing);

        var count = await _sut.WithdrawRoadmapReviewsOfInactiveCreatorsAsync();

        Assert.Equal(0, count);
        Assert.Equal(RoadmapTemplateStatus.Draft, draft.Status);
        Assert.Equal(RoadmapTemplateStatus.Published, published.Status);
        Assert.Equal(ModerationItemStatus.Pending, other.Status);
    }

    [Fact]
    public async Task Withdraw_NothingPendingReturnsZeroWithoutTransaction()
    {
        Assert.Equal(0, await _sut.WithdrawRoadmapReviewsOfInactiveCreatorsAsync());
        _h.UnitOfWork.Verify(unit => unit.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Withdraw_SaveFailureRollsBack()
    {
        PendingReview(1, 40);
        Suspend(Departing);
        _h.UnitOfWork.Setup(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.WithdrawRoadmapReviewsOfInactiveCreatorsAsync());

        _h.UnitOfWork.Verify(unit => unit.RollbackTransactionAsync(CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ReleaseClaimsOfModerator_ReturnsHeldItemsToQueue()
    {
        var held = new ModerationItem
        {
            Id = 60,
            ItemType = ModerationItemType.RoadmapTemplateReview,
            Status = ModerationItemStatus.Claimed,
            ClaimedByUserId = Departing,
            ClaimedAt = DateTime.UtcNow,
            CreatedByUserId = Colleague
        };
        _h.Items.Add(held);

        Assert.Equal(1, await _sut.ReleaseClaimsOfModeratorAsync(Departing));
        Assert.Equal(ModerationItemStatus.Pending, held.Status);
        Assert.Null(held.ClaimedByUserId);
    }
}
