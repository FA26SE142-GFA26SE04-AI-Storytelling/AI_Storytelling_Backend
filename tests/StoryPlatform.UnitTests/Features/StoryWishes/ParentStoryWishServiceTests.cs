using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AIStoryInput.DTOs;
using StoryPlatform.Application.Features.AIStoryInput.Interfaces;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;
using StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;
using StoryPlatform.Application.Features.StoryWishes;
using StoryPlatform.Application.Features.StoryWishes.Interfaces;
using StoryPlatform.Application.Features.StoryWishes.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.UnitTests.Features.ReadingSessions;
using Xunit;

namespace StoryPlatform.UnitTests.Features.StoryWishes;

public class ParentStoryWishServiceTests
{
    private const int Parent = 7;
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 3, 0, 0, TimeSpan.Zero);

    private readonly ReadingHarness _h = new();
    private readonly ManualTimeProvider _clock = new(Now);
    private readonly Mock<ISupervisionAccessGuard> _guard = new();
    private readonly Mock<IChildRoadmapService> _roadmap = new();
    private readonly Mock<IAIStoryInputService> _aiInput = new();
    private readonly Mock<IStoryWishSuggestionService> _suggestions = new();
    private readonly Mock<IAuditLogWriter> _audit = new();
    private readonly ParentStoryWishService _sut;

    private readonly WishCard _cat;
    private readonly WishCard _forest;
    private readonly WishCard _lost;
    private readonly WishCard _share;

    public ParentStoryWishServiceTests()
    {
        _cat = _h.AddCard(WishCardGroup.Character, "Chú mèo");
        _forest = _h.AddCard(WishCardGroup.Place, "Khu rừng");
        _lost = _h.AddCard(WishCardGroup.Challenge, "Bị lạc đường");
        _share = _h.AddCard(WishCardGroup.Lesson, "Chia sẻ");
        _aiInput.Setup(a => a.GetContextAsync(Parent, ReadingHarness.Child, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AIStoryInputContextDto
            {
                ChildProfileId = ReadingHarness.Child, DefaultVocabularyLevel = "level_2", DefaultLanguage = "vi",
                MaximumLength = 1200
            });
        _suggestions.Setup(s => s.ForParentAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int _, int wishId, CancellationToken _) => new ParentWishSuggestionsDto { WishId = wishId });
        _sut = new ParentStoryWishService(_h.UnitOfWork.Object, _guard.Object, _roadmap.Object, _aiInput.Object,
            _suggestions.Object, _audit.Object, _clock);
    }

    private DateTime UtcNow => _clock.GetUtcNow().UtcDateTime;

    private StoryWish Wish(
        StoryWishStatus status = StoryWishStatus.Pending, int childId = ReadingHarness.Child, double daysAgo = 1,
        bool lessonLeft = false, StoryWishKind kind = StoryWishKind.Wish)
    {
        var submitted = UtcNow.AddDays(-daysAgo);
        var wish = new StoryWish
        {
            Id = _h.StoryWishes.Count + 1, ChildProfileId = childId, Kind = kind, Status = status,
            CharacterCardId = _cat.Id, PlaceCardId = _forest.Id, ChallengeCardId = _lost.Id,
            LessonCardId = lessonLeft ? null : _share.Id, LessonLeftToParent = lessonLeft,
            SubmittedAt = submitted, PendingSince = submitted, CreatedAt = submitted
        };
        _h.StoryWishes.Add(wish);
        return wish;
    }

    // ---- ListAsync ----

    [Fact]
    public async Task List_ChecksApproveStory_ForThisChild()
    {
        await _sut.ListAsync(Parent, ReadingHarness.Child);

        _guard.Verify(g => g.EnsurePermissionAsync(
            ReadingHarness.Child, Parent, Permission.ApproveStory, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task List_WaitingWishesComeFirst_DraftsAndOtherChildrenAreHidden()
    {
        var oldDone = Wish(StoryWishStatus.Declined, daysAgo: 1);
        var waiting = Wish(StoryWishStatus.Pending, daysAgo: 3);
        var accepted = Wish(StoryWishStatus.Accepted, daysAgo: 2);
        Wish(StoryWishStatus.Draft);
        Wish(StoryWishStatus.Pending, childId: ReadingHarness.OtherChild);

        var list = await _sut.ListAsync(Parent, ReadingHarness.Child);

        Assert.Equal(new[] { accepted.Id, waiting.Id, oldDone.Id }, list.Select(w => w.WishId));
    }

    [Fact]
    public async Task List_ShowsCardsExpiryAndTheParentOnlyReason()
    {
        var waiting = Wish(StoryWishStatus.Pending, daysAgo: 2);
        var declined = Wish(StoryWishStatus.Declined, daysAgo: 4);
        declined.DeclineReason = "Con đã có truyện tương tự";

        var list = await _sut.ListAsync(Parent, ReadingHarness.Child);

        var first = list.Single(w => w.WishId == waiting.Id);
        Assert.Equal(new[] { "Chú mèo", "Khu rừng", "Bị lạc đường", "Chia sẻ" }, first.Cards.Select(c => c.Label));
        Assert.Equal(waiting.PendingSince!.Value.AddDays(7), first.ExpiresAt);
        Assert.Equal("Con đã có truyện tương tự", list.Single(w => w.WishId == declined.Id).DeclineReason);
        Assert.Null(list.Single(w => w.WishId == declined.Id).ExpiresAt);
    }

    [Fact]
    public async Task List_WithoutPermission_IsForbidden()
    {
        _guard.Setup(g => g.EnsurePermissionAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Permission>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("không đủ quyền"));

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.ListAsync(Parent, ReadingHarness.Child));
    }

    // ---- GetAsync ----

    [Fact]
    public async Task Get_ReturnsTheWishWithItsSuggestions()
    {
        var wish = Wish();

        var detail = await _sut.GetAsync(Parent, wish.Id);

        Assert.Equal(wish.Id, detail.Wish.WishId);
        Assert.Equal(wish.Id, detail.Suggestions.WishId);
    }

    [Fact]
    public async Task Get_UnknownOrDraft_IsNotFound()
    {
        var draft = Wish(StoryWishStatus.Draft);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetAsync(Parent, 404));
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetAsync(Parent, draft.Id));
    }

    // ---- GetGenerationFormAsync ----

    [Fact]
    public async Task GenerationForm_IsPrefilledFromTheCards_AndTheChildsSettings()
    {
        var wish = Wish();

        var form = await _sut.GetGenerationFormAsync(Parent, wish.Id);

        var request = form.Request;
        Assert.Equal("Chú mèo ở Khu rừng: Bị lạc đường", request.Topic);
        Assert.Equal("specified", request.CharacterMode);
        Assert.Equal(new[] { "Chú mèo" }, request.Characters);
        Assert.Equal("specified", request.SettingMode);
        Assert.Equal("Khu rừng", request.Setting);
        Assert.Equal("Chia sẻ", request.Lesson);
        Assert.Equal("level_2", request.VocabularyLevel);
        Assert.Equal("vi", request.Language);
        Assert.Equal(1200, request.TargetLength);
        Assert.Equal(ReadingHarness.Child, request.ChildProfileId);
        Assert.Equal(StoryWishDecisionPolicy.GenerationKey(wish.Id), request.IdempotencyKey);
        Assert.False(form.LessonLeftToParent);
    }

    [Fact]
    public async Task GenerationForm_LessonLeftToParent_LeavesTheLessonForTheParent()
    {
        var wish = Wish(lessonLeft: true);

        var form = await _sut.GetGenerationFormAsync(Parent, wish.Id);

        Assert.Equal(string.Empty, form.Request.Lesson);
        Assert.True(form.LessonLeftToParent);
    }

    [Fact]
    public async Task GenerationForm_NotPending_IsConflict()
    {
        var wish = Wish(StoryWishStatus.Fulfilled);

        await Assert.ThrowsAsync<ConflictException>(() => _sut.GetGenerationFormAsync(Parent, wish.Id));
    }

    // ---- UseExistingAsync ----

    private void Suggest(StoryWish wish, int rank, int storyId, int? communityId = null) =>
        _h.Suggestions.Add(new StoryWishSuggestion
        {
            Id = _h.Suggestions.Count + 1, StoryWishId = wish.Id, StoryId = storyId, Rank = rank, Score = 6,
            MatchedCardIds = $"{_cat.Id}", CommunitySubmissionId = communityId
        });

    [Fact]
    public async Task UseExisting_AddsTheChosenSuggestionToTheRoadmap_AndFulfilsTheWish()
    {
        var wish = Wish();
        Suggest(wish, 1, 31);
        Suggest(wish, 2, 32);

        var result = await _sut.UseExistingAsync(Parent, wish.Id, new UseExistingStoryRequestDto { SuggestionRank = 2 });

        _roadmap.Verify(r => r.AddForWishAsync(Parent, ReadingHarness.Child, 32, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(StoryWishStatus.Fulfilled, wish.Status);
        Assert.Equal(32, wish.FulfilledStoryId);
        Assert.True(wish.FulfilledWithExistingStory);
        Assert.Equal(Parent, wish.ResolvedByUserId);
        Assert.Equal(UtcNow, wish.ResolvedAt);
        Assert.Equal("Fulfilled", result.Status);
        _aiInput.Verify(a => a.SubmitAsync(It.IsAny<int>(), It.IsAny<SubmitAIStoryInputRequestDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UseExisting_ARankOutsideTheList_IsRefused_AndTouchesNoRoadmap()
    {
        var wish = Wish();
        Suggest(wish, 1, 31);

        var error = await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.UseExistingAsync(Parent, wish.Id, new UseExistingStoryRequestDto { SuggestionRank = 3 }));

        Assert.Equal(StoryWishDecisionPolicy.OnlySuggestedMessage, error.Message);
        _roadmap.VerifyNoOtherCalls();
        Assert.Equal(StoryWishStatus.Pending, wish.Status);
    }

    [Fact]
    public async Task UseExisting_ACommunitySuggestion_IsRefusedWithGuidance()
    {
        var wish = Wish();
        Suggest(wish, 1, 80, communityId: 900);

        var error = await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.UseExistingAsync(Parent, wish.Id, new UseExistingStoryRequestDto { SuggestionRank = 1 }));

        Assert.Equal(StoryWishDecisionPolicy.CommunityNotSupportedMessage, error.Message);
        _roadmap.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UseExisting_ACreationWish_IsRefused()
    {
        var wish = Wish(kind: StoryWishKind.Creation);
        Suggest(wish, 1, 31);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.UseExistingAsync(Parent, wish.Id, new UseExistingStoryRequestDto { SuggestionRank = 1 }));
    }

    [Fact]
    public async Task UseExisting_AnAlreadyHandledWish_IsConflict()
    {
        var wish = Wish(StoryWishStatus.Declined);
        Suggest(wish, 1, 31);

        await Assert.ThrowsAsync<ConflictException>(() =>
            _sut.UseExistingAsync(Parent, wish.Id, new UseExistingStoryRequestDto { SuggestionRank = 1 }));
    }

    [Fact]
    public async Task UseExisting_RoadmapRefuses_LeavesTheWishPending()
    {
        var wish = Wish();
        Suggest(wish, 1, 31);
        _roadmap.Setup(r => r.AddForWishAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BadRequestException("Truyện này hiện không hợp với bé."));

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.UseExistingAsync(Parent, wish.Id, new UseExistingStoryRequestDto { SuggestionRank = 1 }));

        Assert.Equal(StoryWishStatus.Pending, wish.Status);
    }

    // ---- CreateStoryAsync ----

    private SubmitAIStoryInputRequestDto Request(int childId = ReadingHarness.Child) => new()
    {
        Topic = "Chú mèo ở Khu rừng", CharacterMode = "specified", Characters = new[] { "Chú mèo" },
        SettingMode = "specified", Setting = "Khu rừng", Lesson = "Chia sẻ", VocabularyLevel = "level_2",
        Language = "vi", TargetLength = 800, ChildProfileId = childId, IdempotencyKey = "story-wish-000001"
    };

    [Fact]
    public async Task CreateStory_SendsTheEditedFormToGeneration_AndAcceptsTheWish()
    {
        var wish = Wish();
        var request = Request();
        _aiInput.Setup(a => a.SubmitAsync(Parent, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AIStoryInputProgressDto { StoryId = 500, RequestId = 77 });

        var result = await _sut.CreateStoryAsync(Parent, wish.Id, request);

        Assert.Equal(StoryWishStatus.Accepted, wish.Status);
        Assert.Equal(500, wish.GeneratedStoryId);
        Assert.Equal(77, wish.GenerationRequestId);
        Assert.Equal(Parent, wish.ResolvedByUserId);
        Assert.Null(wish.ResolvedAt);
        Assert.Equal(500, result.Generation.StoryId);
        Assert.Equal("Accepted", result.Wish.Status);
        _roadmap.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateStory_ForAnotherChild_IsRefusedBeforeGeneration()
    {
        var wish = Wish();

        var error = await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.CreateStoryAsync(Parent, wish.Id, Request(ReadingHarness.OtherChild)));

        Assert.Equal(StoryWishDecisionPolicy.ChildMismatchMessage, error.Message);
        _aiInput.Verify(a => a.SubmitAsync(It.IsAny<int>(), It.IsAny<SubmitAIStoryInputRequestDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateStory_GenerationFails_LeavesTheWishPending()
    {
        var wish = Wish();
        _aiInput.Setup(a => a.SubmitAsync(It.IsAny<int>(), It.IsAny<SubmitAIStoryInputRequestDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConflictException("Hết lượt tạo truyện."));

        await Assert.ThrowsAsync<ConflictException>(() => _sut.CreateStoryAsync(Parent, wish.Id, Request()));

        Assert.Equal(StoryWishStatus.Pending, wish.Status);
        Assert.Null(wish.GeneratedStoryId);
    }

    [Fact]
    public async Task CreateStory_AnAlreadyHandledWish_IsConflict_AndNothingIsGenerated()
    {
        var wish = Wish(StoryWishStatus.Accepted);

        await Assert.ThrowsAsync<ConflictException>(() => _sut.CreateStoryAsync(Parent, wish.Id, Request()));

        _aiInput.Verify(a => a.SubmitAsync(It.IsAny<int>(), It.IsAny<SubmitAIStoryInputRequestDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- DeclineAsync ----

    [Fact]
    public async Task Decline_StoresTheParentOnlyReason()
    {
        var wish = Wish();

        var result = await _sut.DeclineAsync(Parent, wish.Id, new DeclineWishRequestDto { Reason = "  Con đã có truyện này  " });

        Assert.Equal(StoryWishStatus.Declined, wish.Status);
        Assert.Equal("Con đã có truyện này", wish.DeclineReason);
        Assert.Null(wish.ResolutionNote);
        Assert.Equal(UtcNow, wish.ResolvedAt);
        Assert.Equal("Declined", result.Status);
        _audit.Verify(a => a.LogAsync(Parent, "StoryWishDeclined", nameof(StoryWish), wish.Id,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Decline_BlankReasonIsStoredAsNoReason()
    {
        var wish = Wish();

        await _sut.DeclineAsync(Parent, wish.Id, new DeclineWishRequestDto { Reason = "   " });

        Assert.Null(wish.DeclineReason);
    }

    [Fact]
    public async Task Decline_AnAcceptedWish_IsAParentCancellation()
    {
        var wish = Wish(StoryWishStatus.Accepted);

        await _sut.DeclineAsync(Parent, wish.Id, new DeclineWishRequestDto());

        Assert.Equal(StoryWishStatus.Declined, wish.Status);
        Assert.Equal(StoryWishDecisionPolicy.NoteParentCancelled, wish.ResolutionNote);
    }

    [Fact]
    public async Task Decline_TooLongReason_IsBadRequest()
    {
        var wish = Wish();

        var error = await Assert.ThrowsAsync<BadRequestException>(() => _sut.DeclineAsync(Parent, wish.Id,
            new DeclineWishRequestDto { Reason = new string('a', StoryWishDecisionPolicy.MaxDeclineReasonLength + 1) }));

        Assert.Equal(StoryWishDecisionPolicy.ReasonTooLongMessage, error.Message);
        Assert.Equal(StoryWishStatus.Pending, wish.Status);
    }

    [Theory]
    [InlineData(StoryWishStatus.Fulfilled)]
    [InlineData(StoryWishStatus.Declined)]
    [InlineData(StoryWishStatus.Expired)]
    [InlineData(StoryWishStatus.Cancelled)]
    public async Task Decline_AFinishedWish_IsConflict(StoryWishStatus status)
    {
        var wish = Wish(status);

        await Assert.ThrowsAsync<ConflictException>(() => _sut.DeclineAsync(Parent, wish.Id, new DeclineWishRequestDto()));
    }

    [Fact]
    public async Task Decisions_WithoutPermission_AreForbidden_AndChangeNothing()
    {
        var wish = Wish();
        Suggest(wish, 1, 31);
        _guard.Setup(g => g.EnsurePermissionAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Permission>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("không đủ quyền"));

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.DeclineAsync(Parent, wish.Id, new DeclineWishRequestDto()));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.UseExistingAsync(Parent, wish.Id, new UseExistingStoryRequestDto { SuggestionRank = 1 }));
        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.CreateStoryAsync(Parent, wish.Id, Request()));

        Assert.Equal(StoryWishStatus.Pending, wish.Status);
        _roadmap.VerifyNoOtherCalls();
    }
}
