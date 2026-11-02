using Moq;
using StoryPlatform.Application.Features.StoryWishes;
using StoryPlatform.Application.Features.StoryWishes.Interfaces;
using StoryPlatform.Application.Features.StoryWishes.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.UnitTests.Features.ReadingSessions;
using Xunit;

namespace StoryPlatform.UnitTests.Features.StoryWishes;

public class StoryWishSweepServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Prepared_notifications_are_published_only_after_successful_commit(bool failCommit)
    {
        var wish = Wish(StoryWishStatus.Pending, pendingDaysAgo: 6.2);
        var prepared = new PreparedStoryWishNotification(7, new() { Id = 55 });
        _notifier.Setup(x => x.PrepareExpiringAsync(wish, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { prepared });
        var committed = false;
        _h.UnitOfWork.Setup(x => x.CommitTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken _) =>
            {
                if (failCommit) throw new InvalidOperationException("simulated commit failure");
                committed = true;
                return Task.CompletedTask;
            });
        _notifier.Setup(x => x.PublishPreparedAsync(It.IsAny<IReadOnlyList<PreparedStoryWishNotification>>(), It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyList<PreparedStoryWishNotification> rows, CancellationToken _) =>
            { Assert.True(committed); Assert.Equal(prepared, Assert.Single(rows)); });
        if (failCommit)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.SweepAsync());
            _notifier.Verify(x => x.PublishPreparedAsync(It.IsAny<IReadOnlyList<PreparedStoryWishNotification>>(), It.IsAny<CancellationToken>()), Times.Never);
            _h.UnitOfWork.Verify(x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        }
        else
        {
            Assert.Equal(1, (await _sut.SweepAsync()).Warned);
            Assert.Equal(0, (await _sut.SweepAsync()).Warned);
            _notifier.Verify(x => x.PublishPreparedAsync(It.IsAny<IReadOnlyList<PreparedStoryWishNotification>>(), It.IsAny<CancellationToken>()), Times.Once);
        }
    }
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 3, 0, 0, TimeSpan.Zero);

    private readonly ReadingHarness _h = new();
    private readonly ManualTimeProvider _clock = new(Now);
    private readonly Mock<IStoryWishNotifier> _notifier = new();
    private readonly StoryWishSweepService _sut;

    public StoryWishSweepServiceTests()
    {
        _notifier.Setup(x => x.PrepareExpiringAsync(It.IsAny<StoryWish>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PreparedStoryWishNotification>());
        _sut = new StoryWishSweepService(_h.UnitOfWork.Object, _notifier.Object, _clock);
    }

    private DateTime UtcNow => _clock.GetUtcNow().UtcDateTime;

    [Fact]
    public async Task Sweep_RereadsWishAfterLock_DoesNotExpireAcceptedGeneration()
    {
        var wish = Wish(StoryWishStatus.Pending, pendingDaysAgo: 8);
        var story = NewStory(500, StoryStatus.Draft);
        _h.UnitOfWork.Setup(x => x.AcquireTransactionLockAsync(-ReadingHarness.Child, It.IsAny<CancellationToken>()))
            .Callback(() => { wish.Status = StoryWishStatus.Accepted; wish.GeneratedStoryId = story.Id; })
            .Returns(Task.CompletedTask);
        var result = await _sut.SweepAsync();
        Assert.Equal(0, result.Expired);
        Assert.Equal(StoryWishStatus.Accepted, wish.Status);
        Assert.Equal(500, wish.GeneratedStoryId);
    }

    private StoryWish Wish(StoryWishStatus status, double pendingDaysAgo = 1, int childId = ReadingHarness.Child)
    {
        var since = UtcNow.AddDays(-pendingDaysAgo);
        var wish = new StoryWish
        {
            Id = _h.StoryWishes.Count + 1, ChildProfileId = childId, Kind = StoryWishKind.Wish, Status = status,
            SubmittedAt = status == StoryWishStatus.Draft ? null : since,
            PendingSince = status == StoryWishStatus.Draft ? null : since,
            CreatedAt = since
        };
        _h.StoryWishes.Add(wish);
        return wish;
    }

    private Story NewStory(int id, StoryStatus status, DateTime? updatedAt = null, DateTime? archivedAt = null)
    {
        var story = new Story
        {
            Id = id, Title = $"Truyện mới {id}", Scope = StoryScope.Parent, AgeBand = "6-8", Status = status,
            AuthorUserId = 7, ChildProfileId = ReadingHarness.Child, UpdatedAt = updatedAt, ArchivedAt = archivedAt
        };
        _h.Stories.Add(story);
        return story;
    }

    [Fact]
    public async Task Sweep_DeletesDraftsOlderThan24Hours_KeepsNewerOnes()
    {
        Wish(StoryWishStatus.Draft, pendingDaysAgo: 1.1);
        var fresh = Wish(StoryWishStatus.Draft, pendingDaysAgo: 0.5, childId: ReadingHarness.OtherChild);

        var result = await _sut.SweepAsync();

        Assert.Equal(1, result.DraftsDeleted);
        Assert.Equal(new[] { fresh.Id }, _h.StoryWishes.Select(w => w.Id));
    }

    [Fact]
    public async Task Sweep_OnDay6_WarnsOnce_AndDoesNotExpire()
    {
        var wish = Wish(StoryWishStatus.Pending, pendingDaysAgo: 6.2);

        var first = await _sut.SweepAsync();
        var second = await _sut.SweepAsync();

        Assert.Equal(1, first.Warned);
        Assert.Equal(0, second.Warned);
        _notifier.Verify(n => n.PrepareExpiringAsync(wish, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(StoryWishStatus.Pending, wish.Status);
        Assert.Equal(UtcNow, wish.ExpiryWarnedAt);
    }

    [Fact]
    public async Task Sweep_BeforeDay6_DoesNothing()
    {
        var wish = Wish(StoryWishStatus.Pending, pendingDaysAgo: 5.9);

        var result = await _sut.SweepAsync();

        Assert.Equal(0, result.Warned);
        Assert.Equal(StoryWishStatus.Pending, wish.Status);
        _notifier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Sweep_OnDay7_Expires_WithASystemNote()
    {
        var wish = Wish(StoryWishStatus.Pending, pendingDaysAgo: 7);

        var result = await _sut.SweepAsync();

        Assert.Equal(1, result.Expired);
        Assert.Equal(StoryWishStatus.Expired, wish.Status);
        Assert.Equal(StoryWishDecisionPolicy.NoteExpired, wish.ResolutionNote);
        Assert.Equal(UtcNow, wish.ResolvedAt);
        Assert.Equal(StoryWishPolicy.NotThisTimeMessage, StoryWishPolicy.StatusMessage(wish));
    }

    [Fact]
    public async Task Sweep_AcceptedWishWhoseNewStoryIsReady_IsFulfilledAsANewStory()
    {
        var wish = Wish(StoryWishStatus.Accepted, pendingDaysAgo: 8);   // đã nhận thì không còn tính hạn 7 ngày
        NewStory(500, StoryStatus.Ready);
        wish.GeneratedStoryId = 500;

        var result = await _sut.SweepAsync();

        Assert.Equal(1, result.Fulfilled);
        Assert.Equal(StoryWishStatus.Fulfilled, wish.Status);
        Assert.Equal(500, wish.FulfilledStoryId);
        Assert.False(wish.FulfilledWithExistingStory);
        Assert.Equal(StoryWishPolicy.FulfilledNewMessage, StoryWishPolicy.StatusMessage(wish));
    }

    [Fact]
    public async Task Sweep_AcceptedWishStillGenerating_StaysAccepted()
    {
        var wish = Wish(StoryWishStatus.Accepted);
        NewStory(501, StoryStatus.MediaProcessing);
        wish.GeneratedStoryId = 501;

        await _sut.SweepAsync();

        Assert.Equal(StoryWishStatus.Accepted, wish.Status);
    }

    [Theory]
    [InlineData(StoryStatus.Rejected)]
    [InlineData(StoryStatus.Ready)]
    public async Task Sweep_NewStoryRemovedByModerator_DeclinesTheWishImmediately(StoryStatus status)
    {
        var wish = Wish(StoryWishStatus.Accepted);
        NewStory(502, status, archivedAt: UtcNow.AddHours(-1));
        wish.GeneratedStoryId = 502;

        var result = await _sut.SweepAsync();

        Assert.Equal(1, result.Declined);
        Assert.Equal(StoryWishStatus.Declined, wish.Status);
        Assert.Equal(StoryWishDecisionPolicy.NoteStoryArchived, wish.ResolutionNote);
    }

    [Fact]
    public async Task Sweep_NewStoryRejectedByParentRecently_StaysAcceptedDuringGrace()
    {
        var wish = Wish(StoryWishStatus.Accepted);
        NewStory(504, StoryStatus.Rejected, updatedAt: UtcNow.AddDays(-1));
        wish.GeneratedStoryId = 504;

        var result = await _sut.SweepAsync();

        Assert.Equal(0, result.Declined);
        Assert.Equal(StoryWishStatus.Accepted, wish.Status);
    }

    [Fact]
    public async Task Sweep_NewStoryGone_DeclinesTheWish()
    {
        var wish = Wish(StoryWishStatus.Accepted);
        wish.GeneratedStoryId = 999;

        await _sut.SweepAsync();

        Assert.Equal(StoryWishDecisionPolicy.NoteStoryArchived, wish.ResolutionNote);
    }

    [Fact]
    public async Task Sweep_NewStoryRejectedForSevenDaysWithoutANewVersion_DeclinesTheWish()
    {
        var stale = Wish(StoryWishStatus.Accepted);
        NewStory(503, StoryStatus.Rejected, updatedAt: UtcNow.AddDays(-7));
        stale.GeneratedStoryId = 503;
        var recent = Wish(StoryWishStatus.Accepted);
        NewStory(504, StoryStatus.Rejected, updatedAt: UtcNow.AddDays(-3));
        recent.GeneratedStoryId = 504;

        await _sut.SweepAsync();

        Assert.Equal(StoryWishStatus.Declined, stale.Status);
        Assert.Equal(StoryWishDecisionPolicy.NoteStoryRejected, stale.ResolutionNote);
        Assert.Equal(StoryWishStatus.Accepted, recent.Status);
    }

    [Theory]
    [InlineData(ChildProfileStatus.Suspended)]
    [InlineData(ChildProfileStatus.Archived)]
    [InlineData(ChildProfileStatus.Deleted)]
    public async Task Sweep_ChildNoLongerActive_CancelsWaitingWishes_AndDeletesTheDraft(ChildProfileStatus status)
    {
        _h.Children.Single(c => c.Id == ReadingHarness.Child).Status = status;
        var pending = Wish(StoryWishStatus.Pending, pendingDaysAgo: 6.5);
        var accepted = Wish(StoryWishStatus.Accepted);
        var done = Wish(StoryWishStatus.Fulfilled);
        Wish(StoryWishStatus.Draft, pendingDaysAgo: 0.1);

        var result = await _sut.SweepAsync();

        Assert.Equal(2, result.Cancelled);
        Assert.Equal(StoryWishStatus.Cancelled, pending.Status);
        Assert.Equal(StoryWishStatus.Cancelled, accepted.Status);
        Assert.Equal(StoryWishDecisionPolicy.NoteProfileInactive, pending.ResolutionNote);
        Assert.Equal(StoryWishStatus.Fulfilled, done.Status);
        Assert.DoesNotContain(_h.StoryWishes, w => w.Status == StoryWishStatus.Draft);
        _notifier.VerifyNoOtherCalls();   // điều ước đã hủy thì không nhắc nữa
    }

    [Fact]
    public async Task Sweep_SavesOnceAtTheEnd()
    {
        Wish(StoryWishStatus.Pending, pendingDaysAgo: 7);

        await _sut.SweepAsync();

        _h.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
