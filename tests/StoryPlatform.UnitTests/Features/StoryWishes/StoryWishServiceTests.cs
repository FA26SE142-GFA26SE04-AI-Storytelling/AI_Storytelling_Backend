using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.StoryWishes;
using StoryPlatform.Application.Features.StoryWishes.Interfaces;
using StoryPlatform.Application.Features.StoryWishes.Services;
using StoryPlatform.Application.Features.WishCards;
using StoryPlatform.Application.Features.WishCards.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.UnitTests.Features.ReadingSessions;
using Xunit;

namespace StoryPlatform.UnitTests.Features.StoryWishes;

public class StoryWishServiceTests
{
    private static readonly DateTimeOffset Morning = new(2026, 10, 22, 3, 0, 0, TimeSpan.Zero);
    private readonly ReadingHarness _h = new();
    private readonly ManualTimeProvider _clock = new(Morning);
    private readonly Mock<IStoryWishNotifier> _notifier = new();
    private readonly StoryWishService _sut;
    private readonly WishCard _cat;
    private readonly WishCard _forest;
    private readonly WishCard _lost;
    private readonly WishCard _share;

    public StoryWishServiceTests()
    {
        _cat = _h.AddCard(WishCardGroup.Character, "Chú mèo");
        _forest = _h.AddCard(WishCardGroup.Place, "Khu rừng");
        _lost = _h.AddCard(WishCardGroup.Challenge, "Bị lạc đường");
        _share = _h.AddCard(WishCardGroup.Lesson, "Chia sẻ");
        _sut = new StoryWishService(
            _h.UnitOfWork.Object, new ChildWishCardService(_h.UnitOfWork.Object), _notifier.Object, _clock);
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    private StoryWish AddWish(StoryWishStatus status, DateTime? submittedAt = null, int childId = ReadingHarness.Child)
    {
        var wish = new StoryWish
        {
            Id = _h.StoryWishes.Count + 1,
            ChildProfileId = childId,
            Status = status,
            Kind = StoryWishKind.Wish,
            CharacterCardId = _cat.Id,
            PlaceCardId = _forest.Id,
            SubmittedAt = submittedAt,
            CreatedAt = (submittedAt ?? Now).AddMinutes(-5)
        };
        _h.StoryWishes.Add(wish);
        return wish;
    }

    private static SelectWishCardRequestDto Pick(string group, int? cardId, bool leaveToParent = false) =>
        new() { Group = group, CardId = cardId, LeaveToParent = leaveToParent };

    private async Task ReadyDraftAsync()
    {
        await _sut.StartDraftAsync(ReadingHarness.Child);
        await _sut.SelectCardAsync(ReadingHarness.Child, Pick("Character", _cat.Id));
        await _sut.SelectCardAsync(ReadingHarness.Child, Pick("Place", _forest.Id));
    }

    [Fact]
    public async Task StartDraft_CreatesResumesAndExpiresAfter24Hours()
    {
        var first = await _sut.StartDraftAsync(ReadingHarness.Child);
        Assert.False(first.Resumed);
        Assert.Equal(Now.AddHours(24), first.ExpiresAt);
        await _sut.SelectCardAsync(ReadingHarness.Child, Pick("Character", _cat.Id));

        var resumed = await _sut.StartDraftAsync(ReadingHarness.Child);
        Assert.True(resumed.Resumed);
        Assert.Single(resumed.Cards);

        _clock.Advance(TimeSpan.FromHours(25));
        var fresh = await _sut.StartDraftAsync(ReadingHarness.Child);
        Assert.False(fresh.Resumed);
        Assert.True(fresh.ExpiresAt > first.ExpiresAt);
        Assert.Empty(fresh.Cards);
        Assert.Single(_h.StoryWishes);
    }

    [Fact]
    public async Task StartDraft_EnforcesWaitingAndVietnamDailyLimits()
    {
        AddWish(StoryWishStatus.Pending, Now.AddDays(-3));
        AddWish(StoryWishStatus.Accepted, Now.AddDays(-2));
        var waiting = await Assert.ThrowsAsync<ConflictException>(() => _sut.StartDraftAsync(ReadingHarness.Child));
        Assert.Equal(StoryWishPolicy.TooManyWaitingMessage, waiting.Message);

        _h.StoryWishes.Clear();
        AddWish(StoryWishStatus.Declined, StoryWishPolicy.StartOfLocalDayUtc(Now).AddMinutes(1));
        var daily = await Assert.ThrowsAsync<ConflictException>(() => _sut.StartDraftAsync(ReadingHarness.Child));
        Assert.Equal(StoryWishPolicy.DailyLimitMessage, daily.Message);
    }

    [Fact]
    public async Task StartDraft_RequiresActiveEnabledProfile_ButHoldModeDoesNotBlock()
    {
        _h.AddHoldMode(ReadingHarness.Child);
        Assert.NotEqual(0, (await _sut.StartDraftAsync(ReadingHarness.Child)).WishId);
        _h.StoryWishes.Clear();
        _h.Policies.Add(new SafetyPolicy { Id = 1, ChildProfileId = ReadingHarness.Child, StoryWishesEnabled = false });
        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.StartDraftAsync(ReadingHarness.Child));
        _h.Policies.Clear();
        _h.Children.Single(c => c.Id == ReadingHarness.Child).Status = ChildProfileStatus.Suspended;
        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.StartDraftAsync(ReadingHarness.Child));
    }

    [Fact]
    public async Task SelectCard_StoresChangesClearsAndSupportsParentLessonChoice()
    {
        await _sut.StartDraftAsync(ReadingHarness.Child);
        await _sut.SelectCardAsync(ReadingHarness.Child, Pick("Character", _cat.Id));
        var afterPlace = await _sut.SelectCardAsync(ReadingHarness.Child, Pick("Place", _forest.Id));
        await _sut.SelectCardAsync(ReadingHarness.Child, Pick("Challenge", _lost.Id));
        Assert.True(afterPlace.CanSubmit);

        var left = await _sut.SelectCardAsync(ReadingHarness.Child, Pick("Lesson", null, true));
        Assert.True(left.LessonLeftToParent);
        var chosen = await _sut.SelectCardAsync(ReadingHarness.Child, Pick("Lesson", _share.Id));
        Assert.False(chosen.LessonLeftToParent);
        Assert.Equal(_share.Id, _h.StoryWishes.Single().LessonCardId);

        var cleared = await _sut.SelectCardAsync(ReadingHarness.Child, Pick("Character", null));
        Assert.False(cleared.CanSubmit);
    }

    [Fact]
    public async Task SelectCard_RejectsWrongGroupHiddenBlockedAndWrongAgeCards()
    {
        var hidden = _h.AddCard(WishCardGroup.Character, "Ẩn", isActive: false);
        var older = _h.AddCard(WishCardGroup.Character, "Lớn", ageBand: AgeBand.Age_9_10);
        var policy = new SafetyPolicy { Id = 1, ChildProfileId = ReadingHarness.Child, StoryWishesEnabled = true };
        _h.Policies.Add(policy);
        var blocked = _h.AddCard(WishCardGroup.Character, "Chặn", categoryId: 7);
        _h.PolicyCategories.Add(new SafetyPolicyCategory
            { Id = 1, SafetyPolicyId = policy.Id, ContentCategoryId = 7, Rule = PolicyRule.Blocked });
        await _sut.StartDraftAsync(ReadingHarness.Child);

        foreach (var card in new[] { hidden, older, blocked, _forest })
        {
            await Assert.ThrowsAsync<BadRequestException>(() =>
                _sut.SelectCardAsync(ReadingHarness.Child, Pick("Character", card.Id)));
        }
        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.SelectCardAsync(ReadingHarness.Child, Pick("Desire", _cat.Id)));
    }

    [Fact]
    public async Task Submit_RequiresCharacterAndPlace()
    {
        await _sut.StartDraftAsync(ReadingHarness.Child);
        await _sut.SelectCardAsync(ReadingHarness.Child, Pick("Character", _cat.Id));
        var error = await Assert.ThrowsAsync<BadRequestException>(() => _sut.SubmitAsync(ReadingHarness.Child));
        Assert.Equal(StoryWishPolicy.NeedRequiredCardsMessage, error.Message);
        _notifier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Submit_TurnsDraftPendingAndNotifiesInTransaction()
    {
        await ReadyDraftAsync();
        var result = await _sut.SubmitAsync(ReadingHarness.Child);
        var stored = Assert.Single(_h.StoryWishes);
        Assert.Equal(StoryWishStatus.Pending, stored.Status);
        Assert.Equal(Now, stored.SubmittedAt);
        Assert.Equal(StoryWishPolicy.WaitingStatusMessage, result.Message);
        _notifier.Verify(n => n.NotifySubmittedAsync(stored, It.IsAny<CancellationToken>()), Times.Once);
        _h.UnitOfWork.Verify(
            u => u.AcquireTransactionLockAsync(-ReadingHarness.Child, It.IsAny<CancellationToken>()), Times.Once);
        _h.UnitOfWork.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Submit_RechecksLimitsAfterAcquiringTheChildLock()
    {
        await ReadyDraftAsync();
        _h.UnitOfWork.Setup(u => u.AcquireTransactionLockAsync(
                -ReadingHarness.Child, It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                AddWish(StoryWishStatus.Pending, Now.AddDays(-2));
                AddWish(StoryWishStatus.Accepted, Now.AddDays(-3));
            })
            .Returns(Task.CompletedTask);

        var error = await Assert.ThrowsAsync<ConflictException>(() => _sut.SubmitAsync(ReadingHarness.Child));

        Assert.Equal(StoryWishPolicy.TooManyWaitingMessage, error.Message);
        Assert.Single(_h.StoryWishes, wish => wish.Status == StoryWishStatus.Draft);
        _notifier.VerifyNoOtherCalls();
        _h.UnitOfWork.Verify(u => u.RollbackTransactionAsync(CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Submit_RemovesCardsThatBecameUnavailable()
    {
        await ReadyDraftAsync();
        await _sut.SelectCardAsync(ReadingHarness.Child, Pick("Challenge", _lost.Id));
        _lost.IsActive = false;
        var error = await Assert.ThrowsAsync<BadRequestException>(() => _sut.SubmitAsync(ReadingHarness.Child));
        Assert.Equal(StoryWishPolicy.CardUnavailableMessage, error.Message);
        Assert.Null(_h.StoryWishes.Single().ChallengeCardId);
        Assert.Equal(StoryWishStatus.Draft, _h.StoryWishes.Single().Status);
    }

    [Fact]
    public async Task Submit_RechecksLimitsAndRollsBackNotificationFailure()
    {
        await ReadyDraftAsync();
        AddWish(StoryWishStatus.Pending, Now.AddDays(-2));
        AddWish(StoryWishStatus.Pending, Now.AddDays(-1));
        await Assert.ThrowsAsync<ConflictException>(() => _sut.SubmitAsync(ReadingHarness.Child));
        Assert.Single(_h.StoryWishes, w => w.Status == StoryWishStatus.Draft);

        _h.StoryWishes.RemoveAll(w => w.Status != StoryWishStatus.Draft);
        _notifier.Setup(n => n.NotifySubmittedAsync(It.IsAny<StoryWish>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("notify"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.SubmitAsync(ReadingHarness.Child));
        _h.UnitOfWork.Verify(u => u.RollbackTransactionAsync(CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ListMine_ShowsLatestTenAndHidesDraftCancelledAndOtherChildren()
    {
        for (var i = 0; i < 12; i++) AddWish(StoryWishStatus.Declined, Now.AddDays(-20 + i));
        AddWish(StoryWishStatus.Draft);
        AddWish(StoryWishStatus.Cancelled, Now);
        AddWish(StoryWishStatus.Pending, Now, ReadingHarness.OtherChild);

        var list = await _sut.ListMineAsync(ReadingHarness.Child);

        Assert.Equal(StoryWishPolicy.ListLimit, list.Count);
        Assert.Equal(Now.AddDays(-9), list[0].SubmittedAt);
        Assert.All(list, wish => Assert.Equal(StoryWishPolicy.NotThisTimeMessage, wish.Message));
    }

    [Fact]
    public async Task ListMine_KeepsHistoricalHiddenCardsAndWorksWhenFeatureDisabled()
    {
        var wish = AddWish(StoryWishStatus.Fulfilled, Now.AddDays(-1));
        wish.FulfilledStoryId = ReadingHarness.Story;
        wish.FulfilledWithExistingStory = true;
        _cat.IsActive = false;
        _h.Policies.Add(new SafetyPolicy { Id = 1, ChildProfileId = ReadingHarness.Child, StoryWishesEnabled = false });

        var result = Assert.Single(await _sut.ListMineAsync(ReadingHarness.Child));

        Assert.Contains(result.Cards, card => card.Id == _cat.Id);
        Assert.Equal(ReadingHarness.Story, result.FulfilledStoryId);
        Assert.Equal(StoryWishPolicy.FulfilledExistingMessage, result.Message);
    }
}
