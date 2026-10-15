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

    private static StoryService Service(Mock<IGenericRepository<Story>> repo)
    {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.Repository<Story>()).Returns(repo.Object);
        return new StoryService(uow.Object);
    }
}
