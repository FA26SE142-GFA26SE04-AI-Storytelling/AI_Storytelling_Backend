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
    public async Task Compute_AnotherChildsStoryApprovedForCommunity_IsNotSuggested()
    {
        _h.Stories.Add(new Story
        {
            Id = 80, Title = "Truyện Cộng đồng cũ", Scope = StoryScope.Parent, AgeBand = "6-8", Status = StoryStatus.Ready,
            AuthorUserId = 7, ChildProfileId = ReadingHarness.OtherChild
        });
        _h.Match(80, _cat.Id, _sea.Id);
        _h.ModerationItems.Add(new ModerationItem
        {
            Id = 900, ItemType = ModerationItemType.CommunitySubmission, Status = ModerationItemStatus.Approved,
            StoryId = 80, StoryVersionId = 81
        });

        Assert.Equal(0, await _sut.ComputeAsync(Wish()));
        Assert.Empty(_h.Suggestions);
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
    // ---- Xếp hạng theo cảm xúc ----

    // Hồ sơ cảm xúc của truyện: `times` bé khác nhau đọc xong và chọn các cảm xúc này.
    private void OthersFelt(int storyId, int times, params string[] emotions)
    {
        for (var i = 0; i < times; i++)
        {
            Felt(2000 + _h.Reflections.Count, storyId, emotions);
        }
    }

    // Một bé (không phải bé đang gửi điều ước) đọc xong truyện một lần và chọn các cảm xúc này.
    private void Felt(int childId, int storyId, params string[] emotions)
    {
        var session = _h.AddSession(SessionStatus.Completed, childId: childId, storyId: storyId);
        _h.Reflections.Add(new StoryReflection
        {
            Id = _h.Reflections.Count + 1, ReadingSessionId = session.Id, ChildProfileId = childId,
            StoryId = storyId, Emotions = emotions.ToList()
        });
    }

    // Lịch sử của bé: đọc xong một truyện khác (không phải ứng viên), chọn mức thích và cảm xúc.
    private StoryReflection ChildFelt(StoryFeeling feeling, params string[] emotions)
    {
        var session = _h.AddSession(SessionStatus.Completed, storyId: 500 + _h.Sessions.Count);
        session.Feeling = feeling;
        session.CompletedAt = DateTime.UtcNow.AddMinutes(-_h.Sessions.Count);
        var reflection = new StoryReflection
        {
            Id = _h.Reflections.Count + 1, ReadingSessionId = session.Id, ChildProfileId = ReadingHarness.Child,
            StoryId = session.StoryId, Emotions = emotions.ToList()
        };
        _h.Reflections.Add(reflection);
        return reflection;
    }

    private (int[] Stories, int[] Scores) Ranked()
    {
        var rows = _h.Suggestions.OrderBy(s => s.Rank).ToList();
        return (rows.Select(s => s.StoryId).ToArray(), rows.Select(s => s.Score).ToArray());
    }

    [Fact]
    public async Task Compute_StoryWithAnEmotionTheChildAvoids_MovesDown_ButStaysListed()
    {
        _h.AddLibraryStory(31, "Mới", DateTime.UtcNow); _h.Match(31, _cat.Id, _sea.Id);                            // 3+2+1 = 6
        _h.AddLibraryStory(32, "Cũ", DateTime.UtcNow.AddDays(-5)); _h.Match(32, _cat.Id, _sea.Id); // 6
        OthersFelt(31, 3, "Scared");
        ChildFelt(StoryFeeling.Dislike, "Scared");

        await _sut.ComputeAsync(Wish());

        var (stories, scores) = Ranked();
        Assert.Equal(new[] { 32, 31 }, stories);
        Assert.Equal(new[] { 6, 4 }, scores);
    }

    [Fact]
    public async Task Compute_StoryWithAnEmotionTheChildLikes_MovesUp()
    {
        _h.AddLibraryStory(31, "Mới", DateTime.UtcNow); _h.Match(31, _cat.Id, _sea.Id);
        _h.AddLibraryStory(32, "Cũ", DateTime.UtcNow.AddDays(-5)); _h.Match(32, _cat.Id, _sea.Id);
        OthersFelt(32, 3, "Funny");
        ChildFelt(StoryFeeling.VeryLike, "Funny");

        await _sut.ComputeAsync(Wish());

        var (stories, scores) = Ranked();
        Assert.Equal(new[] { 32, 31 }, stories);
        Assert.Equal(new[] { 7, 6 }, scores);
    }

    [Fact]
    public async Task Compute_AvoidedStory_GivesItsPlaceToTheFourthCandidate()
    {
        for (var id = 50; id < 54; id++)
        {
            _h.AddLibraryStory(id, $"T{id}", DateTime.UtcNow.AddDays(-(60 - id)));
            _h.Match(id, _cat.Id, _sea.Id);
        }
        OthersFelt(53, 3, "Sad");
        ChildFelt(StoryFeeling.Dislike, "Sad");

        await _sut.ComputeAsync(Wish());

        Assert.Equal(new[] { 52, 51, 50 }, Ranked().Stories);
    }

    [Fact]
    public async Task Compute_EmotionsNeverLetAStoryBelowMinScoreIn()
    {
        _h.AddLibraryStory(41, "Chỉ khớp nơi chốn"); _h.Match(41, _sea.Id);   // 2 + 1 chưa đọc = 3 < 4
        OthersFelt(41, 3, "Funny");
        ChildFelt(StoryFeeling.VeryLike, "Funny");

        Assert.Equal(0, await _sut.ComputeAsync(Wish()));
        Assert.Empty(_h.Suggestions);
    }

    [Fact]
    public async Task Compute_StoryWithTooFewReflections_IsNotAffected()
    {
        _h.AddLibraryStory(31, "Mới", DateTime.UtcNow); _h.Match(31, _cat.Id, _sea.Id);
        _h.AddLibraryStory(32, "Cũ", DateTime.UtcNow.AddDays(-5)); _h.Match(32, _cat.Id, _sea.Id);
        OthersFelt(31, 2, "Scared");
        ChildFelt(StoryFeeling.Dislike, "Scared");

        await _sut.ComputeAsync(Wish());

        Assert.Equal(new[] { 31, 32 }, Ranked().Stories);
        Assert.Equal(new[] { 6, 6 }, Ranked().Scores);
    }

    [Fact]
    public async Task Compute_DeletedReflections_AreIgnored()
    {
        _h.AddLibraryStory(31, "Mới", DateTime.UtcNow); _h.Match(31, _cat.Id, _sea.Id);
        _h.AddLibraryStory(32, "Cũ", DateTime.UtcNow.AddDays(-5)); _h.Match(32, _cat.Id, _sea.Id);
        OthersFelt(31, 3, "Scared");
        ChildFelt(StoryFeeling.Dislike, "Scared").IsDeleted = true;   // ví dụ sau khi purge

        await _sut.ComputeAsync(Wish());

        Assert.Equal(new[] { 31, 32 }, Ranked().Stories);
    }

    [Fact]
    public async Task Compute_ReadsWithoutAFeeling_DoNotShapeThePreference()
    {
        _h.AddLibraryStory(31, "Mới", DateTime.UtcNow); _h.Match(31, _cat.Id, _sea.Id);
        _h.AddLibraryStory(32, "Cũ", DateTime.UtcNow.AddDays(-5)); _h.Match(32, _cat.Id, _sea.Id);
        OthersFelt(31, 3, "Scared");
        var reflection = ChildFelt(StoryFeeling.Dislike, "Scared");
        _h.Sessions.Single(s => s.Id == reflection.ReadingSessionId).Feeling = null;   // bé chưa chọn mức thích

        await _sut.ComputeAsync(Wish());

        Assert.Equal(new[] { 31, 32 }, Ranked().Stories);
    }

    [Fact]
    public async Task Compute_AvoidedEmotion_DoesNotRemoveAStoryWhoseFinalScoreFallsBelowMinScore()
    {
        _h.AddLibraryStory(31, "Khớp nhân vật"); _h.Match(31, _cat.Id);
        OthersFelt(31, 3, "Scared");
        ChildFelt(StoryFeeling.Dislike, "Scared");

        Assert.Equal(1, await _sut.ComputeAsync(Wish()));
        Assert.Equal(2, Assert.Single(_h.Suggestions).Score);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Compute_DeletedOrUnfinishedSessions_DoNotShapeThePreference(bool deleted)
    {
        _h.AddLibraryStory(31, "Truyện"); _h.Match(31, _cat.Id, _sea.Id);
        OthersFelt(31, 3, "Scared");
        var reflection = ChildFelt(StoryFeeling.Dislike, "Scared");
        var session = _h.Sessions.Single(s => s.Id == reflection.ReadingSessionId);
        if (deleted) session.IsDeleted = true;
        else session.Status = SessionStatus.Reading;

        await _sut.ComputeAsync(Wish());

        Assert.Equal(6, Assert.Single(_h.Suggestions).Score);
    }

    [Fact]
    public async Task Compute_DeletedStoryReflections_DoNotBuildAProfile()
    {
        _h.AddLibraryStory(31, "Truyện"); _h.Match(31, _cat.Id, _sea.Id);
        OthersFelt(31, 3, "Scared");
        _h.Reflections[0].IsDeleted = true;
        ChildFelt(StoryFeeling.Dislike, "Scared");

        await _sut.ComputeAsync(Wish());

        Assert.Equal(6, Assert.Single(_h.Suggestions).Score);
    }

    [Fact]
    public async Task Compute_PreferenceUsesTheTenLatestCompletedSessions_WithIdBreakingTies()
    {
        _h.AddLibraryStory(31, "Truyện"); _h.Match(31, _cat.Id, _sea.Id);
        OthersFelt(31, 3, "Scared");
        var older = ChildFelt(StoryFeeling.Dislike, "Scared");
        var completedAt = DateTime.UtcNow.AddDays(-1);
        _h.Sessions.Single(s => s.Id == older.ReadingSessionId).CompletedAt = completedAt;
        for (var i = 0; i < 10; i++)
        {
            var neutral = ChildFelt(StoryFeeling.Neutral, "Scared");
            _h.Sessions.Single(s => s.Id == neutral.ReadingSessionId).CompletedAt = completedAt;
        }

        await _sut.ComputeAsync(Wish());

        Assert.Equal(6, Assert.Single(_h.Suggestions).Score);
    }

    [Fact]
    public async Task Compute_OneChildRereadingAStory_DoesNotBuildAProfileAlone()
    {
        _h.AddLibraryStory(31, "Mới", DateTime.UtcNow); _h.Match(31, _cat.Id, _sea.Id);
        _h.AddLibraryStory(32, "Cũ", DateTime.UtcNow.AddDays(-5)); _h.Match(32, _cat.Id, _sea.Id);
        // Một bé đọc lại 3 lần, lần nào cũng "Hơi sợ": vẫn chỉ là 1 bé, chưa đủ 3 bé.
        for (var i = 0; i < 3; i++) Felt(ReadingHarness.OtherChild, 31, "Scared");
        ChildFelt(StoryFeeling.Dislike, "Scared");

        await _sut.ComputeAsync(Wish());

        Assert.Equal(new[] { 31, 32 }, Ranked().Stories);
        Assert.Equal(new[] { 6, 6 }, Ranked().Scores);
    }

    [Fact]
    public async Task Compute_RereadsOfOneChild_CountOnce_AgainstOtherChildren()
    {
        _h.AddLibraryStory(31, "Mới", DateTime.UtcNow); _h.Match(31, _cat.Id, _sea.Id);
        _h.AddLibraryStory(32, "Cũ", DateTime.UtcNow.AddDays(-5)); _h.Match(32, _cat.Id, _sea.Id);
        // 4 bé: một bé đọc 3 lần chọn "Hơi sợ" → tính 1/4 = 25%, không gắn; 3 bé khác "Buồn cười".
        for (var i = 0; i < 3; i++) Felt(ReadingHarness.OtherChild, 31, "Scared");
        OthersFelt(31, 3, "Funny");
        ChildFelt(StoryFeeling.Dislike, "Scared");

        await _sut.ComputeAsync(Wish());

        Assert.Equal(new[] { 31, 32 }, Ranked().Stories);
        Assert.Equal(new[] { 6, 6 }, Ranked().Scores);
    }

    [Fact]
    public async Task Compute_EachChildCountsWithTheirLatestReflection()
    {
        _h.AddLibraryStory(31, "Mới", DateTime.UtcNow); _h.Match(31, _cat.Id, _sea.Id);
        _h.AddLibraryStory(32, "Cũ", DateTime.UtcNow.AddDays(-5)); _h.Match(32, _cat.Id, _sea.Id);
        // Bé A: lần trước "Hơi sợ", lần mới nhất "Buồn cười" → tính "Buồn cười".
        // Thêm 1 bé "Hơi sợ", 1 bé "Buồn cười" → "Hơi sợ" 1/3 ≈ 33%, không gắn.
        Felt(ReadingHarness.OtherChild, 31, "Scared");
        Felt(ReadingHarness.OtherChild, 31, "Funny");
        OthersFelt(31, 1, "Scared");
        OthersFelt(31, 1, "Funny");
        ChildFelt(StoryFeeling.Dislike, "Scared");

        await _sut.ComputeAsync(Wish());

        Assert.Equal(new[] { 31, 32 }, Ranked().Stories);
        Assert.Equal(new[] { 6, 6 }, Ranked().Scores);
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
    public async Task ForChild_LegacyCommunitySuggestion_IsLeftOut()
    {
        _h.Suggestions.Add(new StoryWishSuggestion
        {
            Id = 1, StoryWishId = 1, StoryId = ReadingHarness.Story, CommunitySubmissionId = 900, Rank = 1, Score = 6,
            MatchedCardIds = $"{_cat.Id}"
        });
        var wish = Wish();

        Assert.Empty(await _sut.ForChildAsync(wish));
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
