using Microsoft.EntityFrameworkCore;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildRoadmaps;
using StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;
using StoryPlatform.Application.Features.PlanBenefits.Services;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using StoryPlatform.Application.Features.ReadingSessions.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.Persistence;
using StoryPlatform.Infrastructure.Persistence.Repositories;
using Xunit;

namespace StoryPlatform.IntegrationTests.Postgres;

// Opt-in only (STORYPLATFORM_TEST_PG): khoá advisory theo tài khoản khi mở truyện thư viện mới (F-17).
public sealed class PlanBenefitsPostgresTests(AuthoringGuidePostgresFixture fixture) : IClassFixture<AuthoringGuidePostgresFixture>
{
    [PostgresFact]
    public async Task TwoChildrenOpenThirdAndFourthNewStoriesConcurrently_OnlyOneSucceeds()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var owner = database.ParentUserId;
        var children = new List<ChildProfile>();
        var stories = new List<Story>();
        var sessionKeys = new[] { "pg-key-a", "pg-key-b" };
        await using (var seed = new ApplicationDbContext(database.Options))
        {
            for (var i = 0; i < 2; i++)
            {
                children.Add(new ChildProfile
                {
                    OwnerUserId = owner, Nickname = $"Bé {i}", AgeBand = AgeBand.Age_6_8, Language = "vi", Status = ChildProfileStatus.Active
                });
            }

            seed.ChildProfiles.AddRange(children);
            for (var i = 0; i < 4; i++)
            {
                stories.Add(new Story
                {
                    Title = $"Thư viện {i}", Scope = StoryScope.Creator, AgeBand = "6-8", IsPublished = true, PublishedAt = DateTime.UtcNow,
                    Status = StoryStatus.Ready, AuthorUserId = database.ModeratorId
                });
            }

            seed.Stories.AddRange(stories);
            await seed.SaveChangesAsync();
            foreach (var story in stories)
            {
                seed.StoryVersions.Add(new StoryVersion { StoryId = story.Id, VersionNo = 1, Title = story.Title!, Content = "Nội dung", IsCurrent = true });
            }

            for (var i = 0; i < 2; i++)
            {
                var credential = new ChildAccessCredential { ChildProfileId = children[i].Id, AvatarId = "avatar-default", CreatedByUserId = owner };
                seed.ChildAccessCredentials.Add(credential);
                await seed.SaveChangesAsync();
                seed.ChildSessions.Add(new ChildSession
                {
                    ChildProfileId = children[i].Id, SessionKey = sessionKeys[i], LastActivityAt = DateTime.UtcNow,
                    EntryMethod = ChildEntryMethod.EasyLogin, ChildAccessCredentialId = credential.Id
                });
            }

            await seed.SaveChangesAsync();
        }

        // Tài khoản (Miễn phí, 3 truyện mỗi tuần) đã mở 2 truyện mới trong tuần.
        for (var i = 0; i < 2; i++)
        {
            await StartAsync(database.Options, children[0].Id, sessionKeys[0], stories[i].Id);
        }

        var outcomes = await Task.WhenAll(
            Outcome(() => StartAsync(database.Options, children[0].Id, sessionKeys[0], stories[2].Id)),
            Outcome(() => StartAsync(database.Options, children[1].Id, sessionKeys[1], stories[3].Id)));

        Assert.Equal(["Forbidden", "Started"], outcomes.Order());
        await using var db = new ApplicationDbContext(database.Options);
        Assert.Equal(3, await db.ReadingSessions.Select(session => session.StoryId).Distinct().CountAsync());
    }

    private static async Task<string> Outcome(Func<Task> start)
    {
        try
        {
            await start();
            return "Started";
        }
        catch (ForbiddenException)
        {
            return "Forbidden";
        }
    }

    private static async Task StartAsync(DbContextOptions<ApplicationDbContext> options, int childId, string sessionKey, int storyId)
    {
        await using var context = new ApplicationDbContext(options);
        await using var uow = new UnitOfWork(context);
        var settings = new PlanSettingsProvider(uow);
        var service = new ChildReadingService(uow, new AllowAll(), new NoRecorder(), new NoRewards(),
            planBenefits: new PlanBenefitService(uow, settings), planUsage: new PlanUsageService(uow));
        await service.StartAsync(childId, sessionKey, new StartReadingRequestDto { StoryId = storyId });
    }

    private sealed class AllowAll : IChildRoadmapEligibility
    {
        public Task<ChildRoadmapSkipReason?> EvaluateAsync(ChildProfile child, Story? story, CancellationToken cancellationToken = default) =>
            Task.FromResult<ChildRoadmapSkipReason?>(null);

        public Task<ChildRoadmapSkipReason?> EvaluateForParentSelectionAsync(ChildProfile child, Story? story, CancellationToken cancellationToken = default) =>
            Task.FromResult<ChildRoadmapSkipReason?>(null);
    }

    private sealed class NoRecorder : IChildRoadmapProgressRecorder
    {
        public Task<RoadmapCompletionResult?> RecordSessionCompletedAsync(int readingSessionId, bool isRetake, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class NoRewards : IChildRewardService
    {
        public Task<RewardOutcome> OnStoryCompletedAsync(ReadingSession session, bool isRetake, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<RewardOutcome> OnQuizAnsweredAsync(ReadingSession session, bool firstTimeCorrect, bool perfectRunJustCompleted, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<RewardOutcome> OnWordCollectedAsync(ReadingSession session, int wordsInNotebook, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<RewardOutcome> OnRoadmapFinishedAsync(ReadingSession session, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ChildRewardsDto> GetSummaryAsync(int childProfileId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<WaterKnowledgeTreeResultDto> WaterKnowledgeTreeAsync(int childProfileId, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
