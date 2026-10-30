using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Application.Features.ChildRoadmaps;
using StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;
using StoryPlatform.Application.Features.StoryWishes;
using StoryPlatform.Application.Features.StoryWishes.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.UnitTests.Features.ReadingSessions;
using Xunit;

namespace StoryPlatform.UnitTests.Features.StoryWishes;

public class StoryWishSuggestionServiceTests
{
    private readonly ReadingHarness _h = new();
    private readonly Mock<IChildRoadmapEligibility> _eligibility = new();
    private readonly Mock<IChildProfileAccessGuard> _guard = new();
    private readonly StoryWishSuggestionService _sut;

    private readonly WishCard _cat;
    private readonly WishCard _sea;
    private readonly WishCard _lost;
    private readonly WishCard _share;

    public StoryWishSuggestionServiceTests()
    {
        _cat = _h.AddCard(WishCardGroup.Character, "Chú mèo");
        _sea = _h.AddCard(WishCardGroup.Place, "Đáy biển");
        _lost = _h.AddCard(WishCardGroup.Challenge, "Bị lạc đường");
        _share = _h.AddCard(WishCardGroup.Lesson, "Chia sẻ");
        _eligibility.Setup(e => e.EvaluateAsync(It.IsAny<ChildProfile>(), It.IsAny<Story?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChildRoadmapSkipReason?)null);
        _sut = new StoryWishSuggestionService(_h.UnitOfWork.Object, _eligibility.Object, _guard.Object);
    }

    private StoryWish Wish(
        StoryWishStatus status = StoryWishStatus.Pending, StoryWishKind kind = StoryWishKind.Wish, bool lessonLeft = false)
    {
        var wish = new StoryWish
        {
            Id = _h.StoryWishes.Count + 1, ChildProfileId = ReadingHarness.Child, Kind = kind, Status = status,
            CharacterCardId = _cat.Id, PlaceCardId = _sea.Id, ChallengeCardId = _lost.Id,
            LessonCardId = lessonLeft ? null : _share.Id, LessonLeftToParent = lessonLeft,
            SubmittedAt = DateTime.UtcNow.AddHours(-1)
        };
        _h.StoryWishes.Add(wish);
        return wish;
    }

    private void Finished(int storyId) =>
        _h.AddSession(SessionStatus.Completed, storyId: storyId);

    // ---- ComputeAsync ----

    [Fact]
    public async Task Compute_FollowsTheDocumentedExample()
    {
        _h.AddLibraryStory(31, "A"); _h.Match(31, _cat.Id, _sea.Id);                       // 3+2+1 = 6
        _h.AddLibraryStory(32, "B", DateTime.UtcNow.AddDays(-1)); _h.Match(32, _sea.Id, _lost.Id);  // 2+1+1 = 4, mới hơn E
        _h.AddLibraryStory(33, "C"); _h.Match(33, _cat.Id); Finished(33);                  // 3 → không hiện
        _h.AddLibraryStory(34, "D"); _h.Match(34, _lost.Id, _share.Id);                    // không khớp nhân vật / nơi chốn
        _h.AddLibraryStory(35, "E", DateTime.UtcNow.AddDays(-9), readingLevel: 4);         // 3+2+1−2 = 4
        _h.Match(35, _cat.Id, _sea.Id);
        _h.LearningProfiles.Add(new LearningProfile { Id = 1, ChildProfileId = ReadingHarness.Child, ReadingLevel = 2 });
        var wish = Wish();

        var count = await _sut.ComputeAsync(wish);

        Assert.Equal(3, count);
        var rows = _h.Suggestions.OrderBy(s => s.Rank).ToList();
        Assert.Equal(new[] { 31, 32, 35 }, rows.Select(s => s.StoryId));
        Assert.Equal(new[] { 6, 4, 4 }, rows.Select(s => s.Score));
        Assert.Equal(new[] { 1, 2, 3 }, rows.Select(s => s.Rank));
        Assert.Equal($"{_cat.Id},{_sea.Id}", rows[0].MatchedCardIds);
        Assert.All(rows, s => Assert.Equal(wish.Id, s.StoryWishId));
    }

    [Fact]
    public async Task Compute_OnlyCharacterMatch_AndAlreadyRead_IsNotShown()   // AC1
    {
        _h.AddLibraryStory(40, "X"); _h.Match(40, _cat.Id); Finished(40);

        Assert.Equal(0, await _sut.ComputeAsync(Wish()));
        Assert.Empty(_h.Suggestions);
    }

    [Fact]
    public async Task Compute_FourPointsWithoutCharacterOrPlace_IsNotShown()     // AC2
    {
        _h.AddLibraryStory(41, "Y"); _h.Match(41, _lost.Id, _share.Id);

        Assert.Equal(0, await _sut.ComputeAsync(Wish()));
    }

    [Fact]
    public async Task Compute_KeepsOnlyTheTopThree_TiesFavourNewerStories()
    {
        for (var id = 50; id < 55; id++)
        {
            _h.AddLibraryStory(id, $"T{id}", DateTime.UtcNow.AddDays(-(60 - id)));
            _h.Match(id, _cat.Id, _sea.Id);
        }

        await _sut.ComputeAsync(Wish());

        Assert.Equal(new[] { 54, 53, 52 }, _h.Suggestions.OrderBy(s => s.Rank).Select(s => s.StoryId));
    }

    [Fact]
    public async Task Compute_LessonLeftToParent_IsNotScored()
    {
        _h.AddLibraryStory(60, "Z"); _h.Match(60, _sea.Id, _share.Id);   // 2 + 1 chưa đọc = 3 (bài học không tính)

        Assert.Equal(0, await _sut.ComputeAsync(Wish(lessonLeft: true)));
    }

    [Fact]
    public async Task Compute_SkipsStoriesTheChildMayNotOpen()
    {
        _h.AddLibraryStory(70, "Không đủ điều kiện"); _h.Match(70, _cat.Id, _sea.Id);
        _eligibility.Setup(e => e.EvaluateAsync(It.IsAny<ChildProfile>(), It.Is<Story?>(s => s!.Id == 70), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ChildRoadmapSkipReason.BlockedTopic);
        _h.Stories.Add(new Story
        {
            Id = 71, Title = "Truyện riêng của bé khác", Scope = StoryScope.Parent, AgeBand = "6-8", Status = StoryStatus.Ready,
            AuthorUserId = 7, ChildProfileId = ReadingHarness.OtherChild
        });
        _h.Match(71, _cat.Id, _sea.Id);

        Assert.Equal(0, await _sut.ComputeAsync(Wish()));
    }

    [Fact]
    public async Task Compute_ApprovedCommunityStory_IsSuggestedWithItsSubmissionCode()
    {
        _h.Stories.Add(new Story
        {
            Id = 80, Title = "Truyện Cộng đồng", Scope = StoryScope.Parent, AgeBand = "6-8", Status = StoryStatus.Ready,
            AuthorUserId = 7, ChildProfileId = ReadingHarness.OtherChild
        });
        _h.Match(80, _cat.Id, _sea.Id);
        _h.ModerationItems.Add(new ModerationItem
        {
            Id = 900, ItemType = ModerationItemType.CommunitySubmission, Status = ModerationItemStatus.Approved,
            StoryId = 80, StoryVersionId = 81
        });

        await _sut.ComputeAsync(Wish());

        Assert.Equal(900, _h.Suggestions.Single().CommunitySubmissionId);
    }

    [Fact]
    public async Task Compute_ChildsOwnPrivateStory_IsACandidate()
    {
        _h.Match(ReadingHarness.Story, _cat.Id, _sea.Id);   // truyện 10 của chính bé trong harness

        await _sut.ComputeAsync(Wish());

        Assert.Equal(ReadingHarness.Story, _h.Suggestions.Single().StoryId);
        Assert.Null(_h.Suggestions.Single().CommunitySubmissionId);
    }

    [Fact]
    public async Task Compute_RunsOnlyOnce_PerWish()
    {
        _h.AddLibraryStory(90, "Lần 1"); _h.Match(90, _cat.Id, _sea.Id);
        var wish = Wish();
        await _sut.ComputeAsync(wish);
        _h.AddLibraryStory(91, "Thêm sau"); _h.Match(91, _cat.Id, _sea.Id);

        Assert.Equal(0, await _sut.ComputeAsync(wish));
        Assert.Equal(new[] { 90 }, _h.Suggestions.Select(s => s.StoryId));
    }

    [Fact]
    public async Task Compute_CreationWishes_GetNoSuggestions()
    {
        _h.AddLibraryStory(95, "Q"); _h.Match(95, _cat.Id, _sea.Id);

        Assert.Equal(0, await _sut.ComputeAsync(Wish(kind: StoryWishKind.Creation)));
    }

    [Fact]
    public async Task Compute_NeverCallsSaveChanges()
    {
        _h.AddLibraryStory(96, "R"); _h.Match(96, _cat.Id, _sea.Id);

        await _sut.ComputeAsync(Wish());

        _h.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
    // ---- ForChildAsync ----

    private async Task<StoryWish> WithTwoSuggestionsAsync()
    {
        _h.AddLibraryStory(31, "A"); _h.Match(31, _cat.Id, _sea.Id);
        _h.AddLibraryStory(32, "B", DateTime.UtcNow.AddDays(-1)); _h.Match(32, _sea.Id, _lost.Id);
        var wish = Wish();
        await _sut.ComputeAsync(wish);
        return wish;
    }

    [Fact]
    public async Task ForChild_ReturnsTheSavedListInOrder_WithMatchedCardIcons()
    {
        var wish = await WithTwoSuggestionsAsync();

        var list = await _sut.ForChildAsync(wish);

        Assert.Equal(new int?[] { 31, 32 }, list.Select(s => s.StoryId));
        Assert.Equal("A", list[0].Title);
        Assert.Equal("https://cdn.test/cover-31.png", list[0].CoverImageUrl);
        Assert.Equal(new[] { "Chú mèo", "Đáy biển" }, list[0].MatchedCards.Select(c => c.Label));
        Assert.Equal(new[] { "Đáy biển", "Bị lạc đường" }, list[1].MatchedCards.Select(c => c.Label));
    }

    [Theory]
    [InlineData(StoryWishStatus.Fulfilled)]
    [InlineData(StoryWishStatus.Declined)]
    [InlineData(StoryWishStatus.Expired)]
    public async Task ForChild_DisappearsOnceTheWishIsHandled(StoryWishStatus status)
    {
        var wish = await WithTwoSuggestionsAsync();
        wish.Status = status;

        Assert.Empty(await _sut.ForChildAsync(wish));
    }

    [Fact]
    public async Task ForChild_StillShowsWhileAccepted()
    {
        var wish = await WithTwoSuggestionsAsync();
        wish.Status = StoryWishStatus.Accepted;

        Assert.Equal(2, (await _sut.ForChildAsync(wish)).Count);
    }

    [Fact]
    public async Task ForChild_HiddenDuringHoldMode()
    {
        var wish = await WithTwoSuggestionsAsync();
        _h.AddHoldMode(ReadingHarness.Child, triggeringStoryId: ReadingHarness.Story);

        Assert.Empty(await _sut.ForChildAsync(wish));
    }

    [Fact]
    public async Task ForChild_ASuggestedStoryArchivedLater_IsLeftOut()
    {
        var wish = await WithTwoSuggestionsAsync();
        _h.Stories.Single(s => s.Id == 31).ArchivedAt = DateTime.UtcNow;

        Assert.Equal(new int?[] { 32 }, (await _sut.ForChildAsync(wish)).Select(s => s.StoryId));
    }

    [Fact]
    public async Task ForChild_ASuggestedStoryNoLongerAllowedForTheChild_IsLeftOut()
    {
        var wish = await WithTwoSuggestionsAsync();
        // Phụ huynh chặn thêm chủ đề sau khi bé đã gửi điều ước.
        _eligibility.Setup(e => e.EvaluateAsync(It.IsAny<ChildProfile>(), It.Is<Story?>(s => s!.Id == 31), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ChildRoadmapSkipReason.BlockedTopic);

        Assert.Equal(new int?[] { 32 }, (await _sut.ForChildAsync(wish)).Select(s => s.StoryId));
    }

    [Fact]
    public async Task ForChild_CommunityStory_OpensBySubmissionCode_WithoutTheOriginalId()
    {
        _h.Suggestions.Add(new StoryWishSuggestion
        {
            Id = 1, StoryWishId = 1, StoryId = ReadingHarness.Story, CommunitySubmissionId = 900, Rank = 1, Score = 6,
            MatchedCardIds = $"{_cat.Id}"
        });
        var wish = Wish();

        var item = (await _sut.ForChildAsync(wish)).Single();

        Assert.Null(item.StoryId);
        Assert.Equal(900, item.CommunitySubmissionId);
    }

    // ---- ForParentAsync ----

    private const int Parent = 7;

    [Fact]
    public async Task ForParent_ChecksApproveStoryForThisChild()
    {
        var wish = await WithTwoSuggestionsAsync();

        await _sut.ForParentAsync(Parent, wish.Id);

        _guard.Verify(g => g.EnsureOwnerAsync(
            ReadingHarness.Child, Parent, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ForParent_WithoutPermission_IsForbidden()
    {
        var wish = await WithTwoSuggestionsAsync();
        _guard.Setup(g => g.EnsureOwnerAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("không đủ quyền"));

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.ForParentAsync(Parent, wish.Id));
    }

    [Fact]
    public async Task ForParent_ShowsTheSameListWithReadingStatus_AndTheLikedHint()
    {
        var wish = await WithTwoSuggestionsAsync();
        var opened = _h.AddSession(SessionStatus.Reading, storyId: 32);
        opened.StartedAt = DateTime.UtcNow;
        var done = _h.AddSession(SessionStatus.Completed, storyId: 31);
        done.StartedAt = DateTime.UtcNow;
        done.CompletedAt = DateTime.UtcNow;
        done.Feeling = StoryFeeling.VeryLike;

        var view = await _sut.ForParentAsync(Parent, wish.Id);

        Assert.Equal(new int?[] { 31, 32 }, view.Items.Select(i => i.StoryId));
        Assert.True(view.Items[0].Opened);
        Assert.True(view.Items[0].Completed);
        Assert.Equal("VeryLike", view.Items[0].Feeling);
        Assert.Equal("Bé đã đọc A và rất thích — có thể dùng truyện này cho điều ước", view.Items[0].Hint);
        Assert.True(view.Items[1].Opened);
        Assert.False(view.Items[1].Completed);
        Assert.Null(view.Items[1].Hint);
        Assert.Null(view.EmptyMessage);
    }

    [Fact]
    public async Task ForParent_ReadingBeforeTheWishWasSent_DoesNotCountAsOpened()
    {
        var wish = await WithTwoSuggestionsAsync();
        var old = _h.AddSession(SessionStatus.Completed, storyId: 31);
        old.StartedAt = wish.SubmittedAt!.Value.AddDays(-3);
        old.Feeling = StoryFeeling.VeryLike;

        var item = (await _sut.ForParentAsync(Parent, wish.Id)).Items.First();

        Assert.False(item.Opened);
        Assert.Null(item.Hint);
    }

    [Fact]
    public async Task ForParent_ASuggestedStoryNoLongerAllowedForTheChild_IsLeftOut()
    {
        var wish = await WithTwoSuggestionsAsync();
        _eligibility.Setup(e => e.EvaluateAsync(It.IsAny<ChildProfile>(), It.Is<Story?>(s => s!.Id == 32), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ChildRoadmapSkipReason.BlockedTopic);

        var view = await _sut.ForParentAsync(Parent, wish.Id);

        Assert.Equal(new int?[] { 31 }, view.Items.Select(i => i.StoryId));
    }

    [Fact]
    public async Task ForParent_NoSuggestions_ShowsTheDocumentedEmptyMessage()
    {
        var wish = Wish();

        var view = await _sut.ForParentAsync(Parent, wish.Id);

        Assert.Empty(view.Items);
        Assert.Equal(StoryWishSuggestionPolicy.NoSuggestionMessage, view.EmptyMessage);
    }

    [Fact]
    public async Task ForParent_UnknownOrDraftWish_IsNotFound()
    {
        var draft = Wish(StoryWishStatus.Draft);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.ForParentAsync(Parent, 404));
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.ForParentAsync(Parent, draft.Id));
    }
}
