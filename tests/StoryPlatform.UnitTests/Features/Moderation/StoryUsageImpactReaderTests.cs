using System.Linq.Expressions;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.Moderation.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Moderation;

public class StoryUsageImpactReaderTests
{
    private const int StoryId = 10;
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly List<Story> _stories = new();
    private readonly List<WishCard> _cards = new();
    private readonly List<StoryWishCardMatch> _matches = new();
    private readonly List<StoryWishSuggestion> _suggestions = new();
    private readonly List<StoryWish> _wishes = new();
    private readonly List<ChildRoadmapItem> _roadmap = new();
    private readonly StoryUsageImpactReader _sut;

    public StoryUsageImpactReaderTests()
    {
        Back(_stories); Back(_cards); Back(_matches); Back(_suggestions); Back(_wishes); Back(_roadmap);
        _stories.Add(CreatorStory(StoryId));
        _cards.Add(new WishCard { Id = 1, Group = WishCardGroup.Character, Label = "Chú mèo", SortOrder = 1, IsActive = true });
        _cards.Add(new WishCard { Id = 2, Group = WishCardGroup.Place, Label = "Đáy biển", SortOrder = 1, IsActive = false });
        _sut = new StoryUsageImpactReader(_uow.Object);
    }

    private static Story CreatorStory(int id, string ageBand = "6-8", bool published = true) => new()
    {
        Id = id, Title = $"Truyện {id}", Scope = StoryScope.Creator, AgeBand = ageBand,
        Status = StoryStatus.Ready, IsPublished = published, PublishedAt = published ? DateTime.UtcNow : null
    };

    private void Tag(int storyId, int cardId, WishCardMatchSource source = WishCardMatchSource.Moderator) =>
        _matches.Add(new StoryWishCardMatch { Id = _matches.Count + 1, StoryId = storyId, WishCardId = cardId, Source = source });

    [Fact]
    public async Task ReadAsync_MissingStory_ReturnsNull()
    {
        Assert.Null(await _sut.ReadAsync(999, checkRepublish: false));
    }

    [Fact]
    public async Task ReadAsync_ListsTagsInGroupOrder_WithSourceAndActiveFlag()
    {
        Tag(StoryId, 2, WishCardMatchSource.Ai);
        Tag(StoryId, 1);
        _matches.Add(new StoryWishCardMatch { Id = 99, StoryId = StoryId, WishCardId = 1, IsDeleted = true });

        var impact = await _sut.ReadAsync(StoryId, checkRepublish: false);

        Assert.Equal(new[] { 1, 2 }, impact!.WishCards.Select(c => c.WishCardId));
        Assert.Equal("Character", impact.WishCards[0].Group);
        Assert.Equal("Chú mèo", impact.WishCards[0].Label);
        Assert.Equal("Moderator", impact.WishCards[0].Source);
        Assert.Equal("Ai", impact.WishCards[1].Source);
        Assert.False(impact.WishCards[1].IsActive);
    }

    [Fact]
    public async Task ReadAsync_OtherPublishedStoryCount_CountsOnlyOtherPublishedCreatorStoriesOfTheSameAge()
    {
        Tag(StoryId, 1);
        _stories.Add(CreatorStory(20));                       // đếm
        _stories.Add(CreatorStory(21, ageBand: "9-10"));      // khác tuổi
        _stories.Add(CreatorStory(22, published: false));     // chưa công khai
        var archived = CreatorStory(23); archived.ArchivedAt = DateTime.UtcNow; _stories.Add(archived);
        var parentStory = CreatorStory(24); parentStory.Scope = StoryScope.Parent; _stories.Add(parentStory);
        foreach (var id in new[] { 20, 21, 22, 23, 24 }) Tag(id, 1);

        var impact = await _sut.ReadAsync(StoryId, checkRepublish: false);

        Assert.Equal(1, impact!.WishCards.Single().OtherPublishedStoryCount);
    }

    [Fact]
    public async Task ReadAsync_LastStoryOfACard_HasZeroOthers()
    {
        Tag(StoryId, 1);

        var impact = await _sut.ReadAsync(StoryId, checkRepublish: false);

        Assert.Equal(0, impact!.WishCards.Single().OtherPublishedStoryCount);
    }

    [Fact]
    public async Task ReadAsync_PendingWishCount_CountsDistinctPendingOrAcceptedWishes()
    {
        _wishes.AddRange(new[]
        {
            new StoryWish { Id = 1, Status = StoryWishStatus.Pending },
            new StoryWish { Id = 2, Status = StoryWishStatus.Accepted },
            new StoryWish { Id = 3, Status = StoryWishStatus.Fulfilled },
            new StoryWish { Id = 4, Status = StoryWishStatus.Pending, IsDeleted = true }
        });
        void Suggest(int id, int wishId, int? community = null, bool deleted = false) => _suggestions.Add(new StoryWishSuggestion
        {
            Id = id, StoryWishId = wishId, StoryId = StoryId, Rank = 1, MatchedCardIds = "1",
            CommunitySubmissionId = community, IsDeleted = deleted
        });
        Suggest(1, 1); Suggest(2, 1);          // cùng điều ước → tính 1
        Suggest(3, 2);
        Suggest(4, 3);                         // Fulfilled
        Suggest(5, 4);                         // điều ước đã xóa
        _wishes.Add(new StoryWish { Id = 5, Status = StoryWishStatus.Pending });
        Suggest(6, 5, community: 900);         // dòng Community cũ
        _wishes.Add(new StoryWish { Id = 6, Status = StoryWishStatus.Pending });
        Suggest(7, 6, deleted: true);          // dòng gợi ý đã xóa

        var impact = await _sut.ReadAsync(StoryId, checkRepublish: false);

        Assert.Equal(2, impact!.PendingWishCount);
    }

    [Fact]
    public async Task ReadAsync_ActiveRoadmapItemCount_CountsWaitingAndOpenOnly()
    {
        void Step(int id, ChildRoadmapItemStatus status, bool deleted = false, int storyId = StoryId) =>
            _roadmap.Add(new ChildRoadmapItem { Id = id, ChildProfileId = id, StoryId = storyId, Status = status, IsDeleted = deleted });
        Step(1, ChildRoadmapItemStatus.Waiting);
        Step(2, ChildRoadmapItemStatus.Open);
        Step(3, ChildRoadmapItemStatus.Completed);
        Step(4, ChildRoadmapItemStatus.Skipped);
        Step(5, ChildRoadmapItemStatus.Removed);
        Step(6, ChildRoadmapItemStatus.Waiting, deleted: true);
        Step(7, ChildRoadmapItemStatus.Waiting, storyId: 77);

        var impact = await _sut.ReadAsync(StoryId, checkRepublish: false);

        Assert.Equal(2, impact!.ActiveRoadmapItemCount);
    }

    [Theory]
    [InlineData(true, true, true, true)]     // xin công khai lại, có thẻ → nhắc
    [InlineData(false, true, true, false)]   // mục gỡ: không nhắc
    [InlineData(true, false, true, false)]   // chưa từng công khai
    [InlineData(true, true, false, false)]   // không có thẻ
    public async Task ReadAsync_WishCardsNeedReview(bool checkRepublish, bool publishedBefore, bool hasTag, bool expected)
    {
        var story = _stories.Single(s => s.Id == StoryId);
        story.IsPublished = false;
        story.PublishedAt = publishedBefore ? DateTime.UtcNow.AddDays(-3) : null;
        if (hasTag) Tag(StoryId, 1);

        var impact = await _sut.ReadAsync(StoryId, checkRepublish);

        Assert.Equal(expected, impact!.WishCardsNeedReview);
    }

    private void Back<T>(List<T> list) where T : BaseEntity
    {
        var repository = new Mock<IGenericRepository<T>>();
        repository.Setup(r => r.GetByIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object id, CancellationToken _) => list.FirstOrDefault(x => x.Id == (int)id));
        repository.Setup(r => r.CountAsync(It.IsAny<Expression<Func<T, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>>? predicate, CancellationToken _) =>
                predicate == null ? list.Count : list.Count(predicate.Compile()));
        repository.Setup(r => r.FindAsync(It.IsAny<Expression<Func<T, bool>>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>> predicate, string? _, CancellationToken _) =>
                (IReadOnlyList<T>)list.Where(predicate.Compile()).ToList());
        _uow.Setup(u => u.Repository<T>()).Returns(repository.Object);
    }
}
