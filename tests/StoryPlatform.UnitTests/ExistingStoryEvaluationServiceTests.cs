using System.Linq.Expressions;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Learning.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Safety.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.Safety.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;
using StoryPlatform.Application.Features.ExistingStories.DTOs;
using StoryPlatform.Application.Features.ExistingStories.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class ExistingStoryEvaluationServiceTests
{
    [Fact]
    public async Task Evaluate_DoesNotMatchShortOrNumericCategoryIdentifiers()
    {
        var fixture = CreateFixture(
            content: "Lan và Minh cùng nhau đọc một câu chuyện vui.",
            category: new ContentCategory { Id = 1, Code = "a", DisplayName = "1", IsActive = true });

        var result = await fixture.Service.EvaluateAsync(1, 1, 1);

        Assert.Equal(ExistingStoryDecision.Suitable, result.Decision);
        Assert.Empty(result.HardSafetyIssues);
    }

    [Fact]
    public async Task Evaluate_MatchesMeaningfulBlockedCategoryAsWholeTerm()
    {
        var fixture = CreateFixture(
            content: "Câu chuyện có cảnh bạo lực không phù hợp.",
            category: new ContentCategory { Id = 1, Code = "violence", DisplayName = "bạo lực", IsActive = true });

        var result = await fixture.Service.EvaluateAsync(1, 1, 1);

        Assert.Equal(ExistingStoryDecision.Blocked, result.Decision);
        Assert.Single(result.HardSafetyIssues);
        Assert.False(result.CanKeepOriginal);
    }

    [Fact]
    public async Task GetLatest_ReturnsCachedEvaluationWithoutRecomputing()
    {
        var fixture = CreateFixture(
            content: "Lan và Minh cùng nhau đọc một câu chuyện vui.",
            category: new ContentCategory { Id = 1, Code = "violence", DisplayName = "bạo lực", IsActive = true });

        var evaluated = await fixture.Service.EvaluateAsync(1, 1, 1);
        var latest = await fixture.Service.GetLatestAsync(1, 1, 1);

        Assert.Same(evaluated, latest);
        fixture.Safety.Verify(
            service => service.GetSafetyPolicyAsync(1, 1, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static Fixture CreateFixture(string content, ContentCategory category)
    {
        var story = new Story
        {
            Id = 1, ChildProfileId = 1, AuthorUserId = 1, Source = StorySource.Manual,
            Status = StoryStatus.Draft, AgeBand = "9-12", Language = "vi"
        };
        var version = new StoryVersion
        {
            Id = 1, StoryId = 1, VersionNo = 1, Title = "Truyện",
            Content = content, IsCurrent = true
        };

        var storyRepository = new Mock<IGenericRepository<Story>>();
        storyRepository.Setup(repository => repository.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(story);
        var versionRepository = new Mock<IGenericRepository<StoryVersion>>();
        versionRepository.Setup(repository => repository.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(version);
        var categoryRepository = new Mock<IGenericRepository<ContentCategory>>();
        categoryRepository.Setup(repository => repository.FindAsync(
                It.IsAny<Expression<Func<ContentCategory, bool>>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<ContentCategory, bool>> predicate, string? _, CancellationToken _) =>
                new[] { category }.Where(predicate.Compile()).ToArray());

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(store => store.Repository<Story>()).Returns(storyRepository.Object);
        unitOfWork.Setup(store => store.Repository<StoryVersion>()).Returns(versionRepository.Object);
        unitOfWork.Setup(store => store.Repository<ContentCategory>()).Returns(categoryRepository.Object);

        var accessGuard = new Mock<ISupervisionAccessGuard>();
        accessGuard.Setup(guard => guard.EnsurePermissionAsync(
                1, 1, Permission.GenerateStory, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var safety = new Mock<ISafetyPolicyService>();
        safety.Setup(service => service.GetSafetyPolicyAsync(1, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SafetyPolicyDto
            {
                ChildProfileId = 1,
                MaxStoryLength = 5000,
                Categories =
                [
                    new SafetyPolicyCategoryDto
                    {
                        ContentCategoryId = category.Id,
                        Rule = PolicyRule.Blocked.ToString()
                    }
                ]
            });

        var learning = new Mock<ILearningProfileService>();
        learning.Setup(service => service.GetLearningProfileAsync(1, 1, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("LearningProfile"));

        var service = new ExistingStoryEvaluationService(
            unitOfWork.Object,
            accessGuard.Object,
            safety.Object,
            learning.Object,
            new InMemoryExistingStoryEvaluationCache());
        return new Fixture(service, safety);
    }

    private sealed record Fixture(
        ExistingStoryEvaluationService Service,
        Mock<ISafetyPolicyService> Safety);
}
