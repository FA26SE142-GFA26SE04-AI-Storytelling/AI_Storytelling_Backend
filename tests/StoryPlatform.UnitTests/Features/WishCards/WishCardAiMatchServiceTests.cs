using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Application.Features.ChildRoadmaps.Services;
using StoryPlatform.Application.Features.StoryWishes.Services;
using StoryPlatform.Application.Features.WishCards.Interfaces;
using StoryPlatform.Application.Features.WishCards.Services;
using StoryPlatform.Contracts.AI.WishCards;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.UnitTests.Features.ReadingSessions;
using Xunit;

namespace StoryPlatform.UnitTests.Features.WishCards;

public sealed class WishCardAiMatchServiceTests
{
    private readonly ReadingHarness _h = new();
    private readonly Mock<IWishCardMatchingAiClient> _ai = new();
    private readonly Mock<IAuditLogWriter> _audit = new();
    private readonly Mock<IWishCardMappingChangeReader> _changes = new();
    private readonly List<(int Id, int StoryId)> _markers = [];
    private readonly WishCardAiMatchService _service;
    private readonly WishCard _cat;
    private readonly Story _story;
    private const string Content = "A cat walked in the forest.";
    public WishCardAiMatchServiceTests()
    {
        _h.AddUser(11).Role = UserRole.Moderator;
        _story = _h.AddLibraryStory(20, "Cat"); _story.Language = "en";
        _h.Versions.Add(new StoryVersion { Id = 60, StoryId = 20, IsCurrent = true, Title = "Cat", Content = Content });
        _cat = _h.AddCard(WishCardGroup.Character, "cat");
        _ai.Setup(x => x.MatchAsync(It.IsAny<MatchWishCardsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MatchWishCardsRequest request, CancellationToken _) => Response(request));
        _changes.Setup(x => x.ReadWatermarkAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _markers.Count);
        _changes.Setup(x => x.HasChangesAfterAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int storyId, int watermark, CancellationToken _) => _markers.Any(x => x.StoryId == storyId && x.Id > watermark));
        _audit.Setup(x => x.LogAsync(It.IsAny<int?>(), "WishCardMappingsChanged", nameof(Story), It.IsAny<int>(),
                null, It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Callback((int? _, string _, string _, int storyId, object? _, object? _, CancellationToken _) =>
                _markers.Add((_markers.Count + 1, storyId))).Returns(Task.CompletedTask);
        _service = new(_h.UnitOfWork.Object, _ai.Object, _audit.Object, _changes.Object);
    }

    [Fact]
    public async Task Moderator_removal_during_AI_is_not_reintroduced()
    {
        _h.Match(20, _cat.Id);
        var manual = new WishCardMatchService(_h.UnitOfWork.Object, _audit.Object);
        _ai.Setup(x => x.MatchAsync(It.IsAny<MatchWishCardsRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (MatchWishCardsRequest request, CancellationToken ct) =>
            {
                await manual.RemoveAsync(11, 20, _cat.Id, ct);
                return Response(request);
            });
        var error = await Assert.ThrowsAsync<ConflictException>(() => _service.MatchAsync(11, 20, 60, default));
        Assert.Equal("WISH_CARD_MAPPING_STALE", error.Message);
        Assert.Empty(_h.CardMatches);
    }

    [Theory]
    [InlineData(20, true)]
    [InlineData(21, false)]
    public async Task Audit_detects_add_then_remove_even_when_final_mapping_is_identical(int changedStory, bool stale)
    {
        _ai.Setup(x => x.MatchAsync(It.IsAny<MatchWishCardsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MatchWishCardsRequest request, CancellationToken _) =>
            {
                _markers.Add((_markers.Count + 1, changedStory));
                _markers.Add((_markers.Count + 1, changedStory));
                return Response(request);
            });
        if (stale)
        {
            var error = await Assert.ThrowsAsync<ConflictException>(() => _service.MatchAsync(11, 20, 60, default));
            Assert.Equal("WISH_CARD_MAPPING_STALE", error.Message);
            Assert.Empty(_h.CardMatches);
        }
        else Assert.Equal(1, (await _service.MatchAsync(11, 20, 60, default)).AddedCount);
    }

    private MatchWishCardsResponse Response(MatchWishCardsRequest request) => new()
    {
        RequestId = request.RequestId, SourceFingerprint = request.SourceFingerprint, Model = "test-model",
        Matches = [new(_cat.Id, Content, "The cat is a character.")]
    };

    [Fact]
    public async Task Saves_AI_matches_once_and_keeps_existing_Moderator_rows()
    {
        var forest = _h.AddCard(WishCardGroup.Place, "forest");
        _h.CardMatches.Add(new() { Id = 1, StoryId = 20, WishCardId = forest.Id, Source = WishCardMatchSource.Moderator });
        Assert.Equal(1, (await _service.MatchAsync(11, 20, 60, default)).AddedCount);
        Assert.Equal(0, (await _service.MatchAsync(11, 20, 60, default)).AddedCount);
        Assert.Equal(2, _h.CardMatches.Count);
        Assert.Equal(WishCardMatchSource.Moderator, _h.CardMatches.Single(x => x.WishCardId == forest.Id).Source);
        Assert.Equal(WishCardMatchSource.Ai, _h.CardMatches.Single(x => x.WishCardId == _cat.Id).Source);
        _audit.Verify(x => x.LogAsync(11, "WishCardsMatchedByAi", nameof(Story), 20, null, It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Theory]
    [InlineData("content")]
    [InlineData("catalog")]
    [InlineData("version")]
    [InlineData("authority")]
    public async Task Rejects_stale_results_before_writing(string change)
    {
        _ai.Setup(x => x.MatchAsync(It.IsAny<MatchWishCardsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MatchWishCardsRequest request, CancellationToken _) =>
            {
                if (change == "content") _h.Versions.Single(x => x.Id == 60).Content += " Changed.";
                if (change == "catalog") _cat.IsActive = false;
                if (change == "version") _h.Versions.Single(x => x.Id == 60).IsCurrent = false;
                if (change == "authority") _h.Users.Single().Status = AccountStatus.Suspended;
                return Response(request);
            });
        if (change == "authority") await Assert.ThrowsAsync<ForbiddenException>(() => _service.MatchAsync(11, 20, 60, default));
        else await Assert.ThrowsAsync<ConflictException>(() => _service.MatchAsync(11, 20, 60, default));
        Assert.Empty(_h.CardMatches);
        _h.UnitOfWork.Verify(x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("private")]
    [InlineData("version")]
    [InlineData("status")]
    public async Task Access_and_source_checks_run_before_AI(string invalid)
    {
        if (invalid == "actor") _h.Users.Single().Role = UserRole.User;
        if (invalid == "private") _story.Scope = StoryScope.Parent;
        if (invalid == "status") _story.Status = StoryStatus.Draft;
        await Assert.ThrowsAnyAsync<AppException>(() => _service.MatchAsync(11, 20, invalid == "version" ? 999 : 60, default));
        _ai.Verify(x => x.MatchAsync(It.IsAny<MatchWishCardsRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Does_not_trust_an_unknown_card_ID_from_AI()
    {
        _ai.Setup(x => x.MatchAsync(It.IsAny<MatchWishCardsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MatchWishCardsRequest request, CancellationToken _) => Response(request) with
            { Matches = [new(999, Content, "Unsupported ID")] });
        Assert.Equal(502, (await Assert.ThrowsAsync<WishCardAiException>(() => _service.MatchAsync(11, 20, 60, default))).StatusCode);
        Assert.Empty(_h.CardMatches);
    }

    [Fact]
    public async Task Persisted_matches_feed_the_existing_wish_recommendation_algorithm()
    {
        var forest = _h.AddCard(WishCardGroup.Place, "forest");
        _h.CardMatches.Add(new() { Id = 1, StoryId = 20, WishCardId = forest.Id, Source = WishCardMatchSource.Moderator });
        await _service.MatchAsync(11, 20, 60, default);
        var wish = new StoryWish { Id = 25, ChildProfileId = ReadingHarness.Child,
            CharacterCardId = _cat.Id, PlaceCardId = forest.Id, Status = StoryWishStatus.Pending };
        var suggestions = new StoryWishSuggestionService(_h.UnitOfWork.Object, new ChildRoadmapEligibility(_h.UnitOfWork.Object),
            new Mock<IChildProfileAccessGuard>().Object);
        Assert.Equal(1, await suggestions.ComputeAsync(wish));
        Assert.Equal(20, Assert.Single(_h.Suggestions).StoryId);
    }
}
