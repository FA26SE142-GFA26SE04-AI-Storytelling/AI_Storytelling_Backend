using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Abstractions.AI;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.Stories.DTOs;
using StoryPlatform.Application.Features.Stories.Services;
using StoryPlatform.Application.Features.StoryReview.Services;
using StoryPlatform.Application.Features.StoryReview.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.Persistence;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class StoryScopeBoundaryTests
{
    [Fact]
    public void ParentContext_RequiresARealChild_AndRejectsCreator()
    {
        Assert.Throws<ConflictException>(() => StoryPlatform.Application.Common.ParentStoryContext.RequireChildId(new Story()));
        Assert.Throws<NotFoundException>(() => StoryPlatform.Application.Common.ParentStoryContext.RequireChildId(
            new Story { Scope = StoryScope.Creator, ChildProfileId = 3 }));
        Assert.Equal(3, StoryPlatform.Application.Common.ParentStoryContext.RequireChildId(new Story { ChildProfileId = 3 }));
    }

    [Fact]
    public async Task ParentAutoApproval_DoesNotEvaluateOrApproveCreator()
    {
        var repo = new Mock<IGenericRepository<Story>>();
        repo.Setup(x => x.FirstOrDefaultAsync(It.IsAny<Expression<Func<Story, bool>>>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Story { Id = 2, Scope = StoryScope.Creator, Status = StoryStatus.ContentReview });
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.Repository<Story>()).Returns(repo.Object);
        var ai = new Mock<IAIStoryGenerationClient>(MockBehavior.Strict);
        var service = new StoryReviewService(uow.Object, ai.Object, Mock.Of<IProposalCache>());

        Assert.Null(await service.EvaluateAndApplyAutoPublishAsync(2));

        ai.VerifyNoOtherCalls();
        uow.Verify(x => x.Repository<SafetyPolicy>(), Times.Never());
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never());
        repo.Verify(x => x.Update(It.IsAny<Story>()), Times.Never());
    }

    [Fact]
    public async Task LegacyList_ExcludesCreator_EvenWhenCallerRequestsPublishedOrOwnStories()
    {
        var repo = new Mock<IGenericRepository<Story>>();
        var parent = new Story { Id = 1, AuthorUserId = 7, IsPublished = true };
        var creator = new Story { Id = 2, AuthorUserId = 7, Scope = StoryScope.Creator, IsPublished = true };
        repo.Setup(x => x.GetPagedAsync(It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<Expression<Func<Story, bool>>>(), It.IsAny<Func<IQueryable<Story>, IOrderedQueryable<Story>>>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns((int _, int _, Expression<Func<Story, bool>> filter,
                Func<IQueryable<Story>, IOrderedQueryable<Story>> _, string? _, CancellationToken _) =>
            {
                IReadOnlyList<Story> rows = new[] { parent, creator }.Where(filter.Compile()).ToArray();
                return Task.FromResult((rows, rows.Count));
            });
        var service = Service(repo);

        var result = await service.GetStoriesAsync(new StoryFilterRequestDto { IsPublished = true, AuthorUserId = 7 });

        Assert.Equal(1, Assert.Single(result.Items).Id);
    }

    [Fact]
    public async Task CommunityList_ReturnsOnlyPublishedReadyCreatorStories()
    {
        var repo = new Mock<IGenericRepository<Story>>();
        var rows = new[]
        {
            new Story { Id = 1, Scope = StoryScope.Parent, IsPublished = true, Status = StoryStatus.Ready },
            new Story { Id = 2, Scope = StoryScope.Creator, IsPublished = true, Status = StoryStatus.Ready },
            new Story { Id = 3, Scope = StoryScope.Creator, IsPublished = false, Status = StoryStatus.Ready },
            new Story { Id = 4, Scope = StoryScope.Creator, IsPublished = true, Status = StoryStatus.ContentReview },
            new Story { Id = 5, Scope = StoryScope.Creator, IsPublished = true, Status = StoryStatus.Ready, ArchivedAt = DateTime.UtcNow },
            new Story { Id = 6, Scope = StoryScope.Creator, IsPublished = true, Status = StoryStatus.Ready, IsDeleted = true },
        };
        repo.Setup(x => x.GetPagedAsync(It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<Expression<Func<Story, bool>>>(), It.IsAny<Func<IQueryable<Story>, IOrderedQueryable<Story>>>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns((int _, int _, Expression<Func<Story, bool>> filter,
                Func<IQueryable<Story>, IOrderedQueryable<Story>> _, string? _, CancellationToken _) =>
            {
                IReadOnlyList<Story> found = rows.Where(filter.Compile()).ToArray();
                return Task.FromResult((found, found.Count));
            });

        var result = await Service(repo).GetPublishedCreatorStoriesAsync(new StoryFilterRequestDto());

        Assert.Equal(2, Assert.Single(result.Items).Id);
    }

    [Fact]
    public async Task CommunityList_ReturnsViewAndFavoriteCountsPerStory()
    {
        var older = PublishedCreatorStory(10, 7, new DateTime(2026, 11, 1));
        var newer = PublishedCreatorStory(11, 8, new DateTime(2026, 11, 2));
        var sessions = new[]
        {
            new ReadingSession { StoryId = 10, ChildProfileId = 1, Status = SessionStatus.Completed },
            new ReadingSession { StoryId = 10, ChildProfileId = 1, Status = SessionStatus.Abandoned },
            new ReadingSession { StoryId = 10, ChildProfileId = 2, Status = SessionStatus.Started },
            new ReadingSession { StoryId = 11, ChildProfileId = 2, Status = SessionStatus.Completed },
            new ReadingSession { StoryId = 99, ChildProfileId = 2, Status = SessionStatus.Completed },
        };
        var progress = new[]
        {
            new ReadingProgress { StoryId = 10, ChildProfileId = 1, IsFavorited = true },
            new ReadingProgress { StoryId = 10, ChildProfileId = 2, IsFavorited = false },
            new ReadingProgress { StoryId = 11, ChildProfileId = 1, IsFavorited = true },
            new ReadingProgress { StoryId = 11, ChildProfileId = 2, IsFavorited = true },
        };

        var result = await Service(PagedStoryRepo(older, newer), sessions, progress)
            .GetPublishedCreatorStoriesAsync(new StoryFilterRequestDto());

        Assert.Equal(new[] { (10, 3, 1), (11, 1, 2) },
            result.Items.Select(item => (item.Id, item.ViewCount, item.FavoriteCount)));
    }

    [Theory]
    [InlineData(null, false, new[] { 10, 11, 12 })]
    [InlineData("views", true, new[] { 11, 12, 10 })]
    [InlineData("Views", false, new[] { 10, 12, 11 })]
    [InlineData("favorites", true, new[] { 10, 12, 11 })]
    [InlineData("favorites", false, new[] { 11, 12, 10 })]
    [InlineData("unknown", true, new[] { 12, 11, 10 })]
    public async Task CommunityList_SortsByViewsOrFavorites(string? orderBy, bool descending, int[] expected)
    {
        var stories = new[]
        {
            PublishedCreatorStory(10, 7, new DateTime(2026, 11, 1)),
            PublishedCreatorStory(11, 7, new DateTime(2026, 11, 2)),
            PublishedCreatorStory(12, 7, new DateTime(2026, 11, 3)),
        };
        var sessions = new[]
        {
            new ReadingSession { StoryId = 10, ChildProfileId = 1 },
            new ReadingSession { StoryId = 11, ChildProfileId = 1 },
            new ReadingSession { StoryId = 11, ChildProfileId = 2 },
            new ReadingSession { StoryId = 11, ChildProfileId = 3 },
            new ReadingSession { StoryId = 12, ChildProfileId = 1 },
        };
        var progress = new[]
        {
            new ReadingProgress { StoryId = 10, ChildProfileId = 1, IsFavorited = true },
            new ReadingProgress { StoryId = 10, ChildProfileId = 2, IsFavorited = true },
            new ReadingProgress { StoryId = 12, ChildProfileId = 1, IsFavorited = true },
            new ReadingProgress { StoryId = 11, ChildProfileId = 1, IsFavorited = false },
        };

        var result = await Service(PagedStoryRepo(stories), sessions, progress)
            .GetPublishedCreatorStoriesAsync(new StoryFilterRequestDto { OrderBy = orderBy, IsDescending = descending });

        Assert.Equal(expected, result.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task CommunityList_ReturnsAverageRatingFromPublishedRatingsOnly()
    {
        var rated = PublishedCreatorStory(10, 7, new DateTime(2026, 11, 1));
        var unrated = PublishedCreatorStory(11, 7, new DateTime(2026, 11, 2));
        var ratings = new[]
        {
            new StoryRating { StoryId = 10, ReviewerUserId = 1, Rating = 5, Status = StoryRatingStatus.Published },
            new StoryRating { StoryId = 10, ReviewerUserId = 2, Rating = 4, Status = StoryRatingStatus.Published },
            new StoryRating { StoryId = 10, ReviewerUserId = 3, Rating = 1, Status = StoryRatingStatus.PendingModeration },
            new StoryRating { StoryId = 10, ReviewerUserId = 4, Rating = 1, Status = StoryRatingStatus.Hidden },
        };

        var result = await Service(PagedStoryRepo(rated, unrated), ratings: ratings)
            .GetPublishedCreatorStoriesAsync(new StoryFilterRequestDto());

        Assert.Equal(new (int, double?, int)[] { (10, 4.5, 2), (11, null, 0) },
            result.Items.Select(item => (item.Id, item.AverageRating, item.RatingCount)));
    }

    [Theory]
    [InlineData(true, new[] { 11, 10, 12 })]
    [InlineData(false, new[] { 12, 10, 11 })]
    public async Task CommunityList_SortsByAverageRatingThenCount(bool descending, int[] expected)
    {
        var stories = new[]
        {
            PublishedCreatorStory(10, 7, new DateTime(2026, 11, 1)),
            PublishedCreatorStory(11, 7, new DateTime(2026, 11, 2)),
            PublishedCreatorStory(12, 7, new DateTime(2026, 11, 3)),
        };
        // 10: trung bình 4 (1 lượt); 11: trung bình 4 (2 lượt); 12: chưa có đánh giá hiện (= 0).
        var ratings = new[]
        {
            new StoryRating { StoryId = 10, ReviewerUserId = 1, Rating = 4, Status = StoryRatingStatus.Published },
            new StoryRating { StoryId = 11, ReviewerUserId = 1, Rating = 5, Status = StoryRatingStatus.Published },
            new StoryRating { StoryId = 11, ReviewerUserId = 2, Rating = 3, Status = StoryRatingStatus.Published },
            new StoryRating { StoryId = 12, ReviewerUserId = 1, Rating = 5, Status = StoryRatingStatus.Hidden },
        };

        var result = await Service(PagedStoryRepo(stories), ratings: ratings)
            .GetPublishedCreatorStoriesAsync(new StoryFilterRequestDto { OrderBy = "Rating", IsDescending = descending });

        Assert.Equal(expected, result.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task MyPublishedList_UsesCallerAsAuthor_IgnoringRequestedAuthor()
    {
        var mine = PublishedCreatorStory(20, 7, new DateTime(2026, 11, 1));
        var theirs = PublishedCreatorStory(21, 8, new DateTime(2026, 11, 2));
        var myDraft = new Story { Id = 22, AuthorUserId = 7, Scope = StoryScope.Creator, Status = StoryStatus.Draft };
        var sessions = new[] { new ReadingSession { StoryId = 20, ChildProfileId = 1 } };

        var result = await Service(PagedStoryRepo(mine, theirs, myDraft), sessions)
            .GetMyPublishedCreatorStoriesAsync(7, new StoryFilterRequestDto { AuthorUserId = 8 });

        var item = Assert.Single(result.Items);
        Assert.Equal((20, 1, 0), (item.Id, item.ViewCount, item.FavoriteCount));
    }

    [Theory]
    [InlineData("read")]
    [InlineData("update")]
    [InlineData("delete")]
    [InlineData("publish")]
    public async Task LegacyEndpoints_RejectCreator_EvenForAuthorAndReadyStory(string command)
    {
        var repo = new Mock<IGenericRepository<Story>>();
        var creator = new Story { Id = 2, AuthorUserId = 7, Scope = StoryScope.Creator,
            Status = StoryStatus.Ready, Content = "private creator draft" };
        repo.Setup(x => x.GetByIdAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync(creator);
        repo.Setup(x => x.FirstOrDefaultAsync(It.IsAny<Expression<Func<Story, bool>>>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns((Expression<Func<Story, bool>> filter, string? _, CancellationToken _) =>
                Task.FromResult(filter.Compile()(creator) ? creator : null));
        var service = Service(repo);

        await Assert.ThrowsAsync<NotFoundException>(async () =>
        {
            switch (command)
            {
                case "read": await service.GetStoryByIdAsync(2); break;
                case "update": await service.UpdateStoryAsync(2, 7, new UpdateStoryRequestDto { Content = "replace" }); break;
                case "delete": await service.DeleteStoryAsync(2, 7); break;
                case "publish": await service.PublishStoryAsync(2, 7); break;
            }
        });

        repo.Verify(x => x.Update(It.IsAny<Story>()), Times.Never());
        Assert.False(creator.IsPublished);
        Assert.False(creator.IsDeleted);
        Assert.Equal("private creator draft", creator.Content);
    }

    [Fact]
    public async Task ParentStory_PreservesLegacyPublishBehavior()
    {
        var repo = new Mock<IGenericRepository<Story>>();
        var story = new Story { Id = 1, AuthorUserId = 7, ChildProfileId = 3, Status = StoryStatus.Ready };
        repo.Setup(x => x.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(story);

        Assert.True(await Service(repo).PublishStoryAsync(1, 7));

        Assert.True(story.IsPublished);
        repo.Verify(x => x.Update(story), Times.Once());
    }

    [Fact]
    public void SharedStoryModel_ScopeDefaultsToParent_WithoutDatabaseAccess()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model_metadata_only;Username=unused;Password=unused").Options;
        using var context = new ApplicationDbContext(options);
        var scope = context.Model.FindEntityType(typeof(Story))!.FindProperty(nameof(Story.Scope))!;
        Assert.Equal(StoryScope.Parent, scope.GetDefaultValue());
        Assert.Equal("Creator", scope.GetTypeMapping().Converter!.ConvertToProvider(StoryScope.Creator));
        Assert.Equal(StoryScope.Parent, new Story().Scope);
        // Creator can omit a child; Parent context and the DB check retain its requirement.
        Assert.True(context.Model.FindEntityType(typeof(Story))!.FindProperty(nameof(Story.ChildProfileId))!.IsNullable);
    }

    private static StoryService Service(Mock<IGenericRepository<Story>> repo,
        ReadingSession[]? sessions = null, ReadingProgress[]? progress = null, StoryRating[]? ratings = null)
    {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.Repository<Story>()).Returns(repo.Object);
        uow.Setup(x => x.Repository<ReadingSession>()).Returns(ReadOnlyRepo(sessions ?? []).Object);
        uow.Setup(x => x.Repository<ReadingProgress>()).Returns(ReadOnlyRepo(progress ?? []).Object);
        uow.Setup(x => x.Repository<StoryRating>()).Returns(ReadOnlyRepo(ratings ?? []).Object);
        return new StoryService(uow.Object);
    }

    private static Mock<IGenericRepository<T>> ReadOnlyRepo<T>(T[] rows) where T : class
    {
        var repo = new Mock<IGenericRepository<T>>();
        repo.Setup(x => x.Query()).Returns(rows.AsQueryable());
        repo.Setup(x => x.FindAsync(It.IsAny<Expression<Func<T, bool>>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns((Expression<Func<T, bool>> filter, string? _, CancellationToken _) =>
                Task.FromResult<IReadOnlyList<T>>(rows.Where(filter.Compile()).ToArray()));
        return repo;
    }

    private static Mock<IGenericRepository<Story>> PagedStoryRepo(params Story[] stories)
    {
        var repo = new Mock<IGenericRepository<Story>>();
        repo.Setup(x => x.GetPagedAsync(It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<Expression<Func<Story, bool>>>(), It.IsAny<Func<IQueryable<Story>, IOrderedQueryable<Story>>>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns((int page, int size, Expression<Func<Story, bool>> filter,
                Func<IQueryable<Story>, IOrderedQueryable<Story>>? orderBy, string? _, CancellationToken _) =>
            {
                var query = stories.AsQueryable().Where(filter);
                var total = query.Count();
                IReadOnlyList<Story> rows = (orderBy is null ? query : orderBy(query))
                    .Skip((page - 1) * size).Take(size).ToArray();
                return Task.FromResult((rows, total));
            });
        return repo;
    }

    private static Story PublishedCreatorStory(int id, int authorUserId, DateTime createdAt) => new()
    {
        Id = id, AuthorUserId = authorUserId, Scope = StoryScope.Creator, IsPublished = true,
        Status = StoryStatus.Ready, CreatedAt = createdAt
    };
}
