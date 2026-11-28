using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Time;
using StoryPlatform.Application.Features.ChildRoadmaps;
using StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;
using StoryPlatform.Application.Features.Events.Interfaces;
using StoryPlatform.Application.Features.PlanBenefits.DTOs;
using StoryPlatform.Application.Features.PlanBenefits.Interfaces;
using StoryPlatform.Application.Features.PlanBenefits.Services;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using StoryPlatform.Application.Features.ReadingSessions.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.UnitTests.Features.TokenQuota;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ReadingSessions;

/// <summary>F-17: mức Miễn phí mở tối đa N truyện thư viện mới mỗi tuần, tính theo tài khoản (Luồng 2, 2h; Luồng 4).</summary>
public sealed class ChildReadingLibraryLimitTests
{
    private const int OwnerId = 7;
    private const int LibraryStory = 30;
    private const int LibraryVersion = 300;
    private static readonly DateTime CycleEnd = new(2026, 12, 15, 17, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime CycleStart = CycleEnd.AddMonths(-1);
    private static readonly DateTime Now = CycleEnd.AddDays(-2);

    private readonly ReadingHarness _h = new();
    private readonly InMemoryUnitOfWork _plans = new();
    private readonly Mock<IPlanBenefitService> _benefits = new();
    private readonly Mock<IPlanUsageService> _usage = new();
    private readonly Mock<IEventIntegrationHooks> _eventHooks = new();
    private readonly ChildReadingService _sut;

    public ChildReadingLibraryLimitTests()
    {
        _h.AddLibraryStory(LibraryStory, "Truyện thư viện");
        _h.Versions.Add(new StoryVersion { Id = LibraryVersion, StoryId = LibraryStory, VersionNo = 1, Title = "Truyện thư viện", IsCurrent = true });
        WeeklyLimit(3);
        NewThisWeek(3);
        _usage.Setup(u => u.HasOpenedLibraryStoryAsync(OwnerId, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _eventHooks.Setup(h => h.IsStoryExemptFromWeeklyLimitAsync(OwnerId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _sut = CreateService(_benefits.Object, _usage.Object);
        _plans.Seed(new UserAccount { Id = OwnerId, Role = UserRole.User });
    }

    private ChildReadingService CreateService(IPlanBenefitService benefits, IPlanUsageService usage)
    {
        var eligibility = new Mock<IChildRoadmapEligibility>();
        eligibility.Setup(e => e.EvaluateAsync(It.IsAny<ChildProfile>(), It.IsAny<Story?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChildRoadmapSkipReason?)null);
        return new ChildReadingService(_h.UnitOfWork.Object, eligibility.Object,
            new Mock<IChildRoadmapProgressRecorder>().Object, new Mock<IChildRewardService>().Object,
            planBenefits: benefits, planUsage: usage, eventHooks: _eventHooks.Object);
    }

    private void WeeklyLimit(int? limit) => _benefits
        .Setup(b => b.GetForUserAsync(OwnerId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new PlanBenefitsDto
        {
            MaxChildProfiles = 4, WeeklyNewLibraryStories = limit,
            NewLibraryStoriesPeriodStart = VietnamTime.StartOfWeekUtc(Now),
            NewLibraryStoriesResetsAt = VietnamTime.StartOfNextWeekUtc(Now)
        });

    private void NewThisWeek(int count)
    {
        _usage
            .Setup(u => u.CountNewLibraryStoriesInPeriodAsync(
                OwnerId, VietnamTime.StartOfWeekUtc(Now), VietnamTime.StartOfNextWeekUtc(Now), It.IsAny<CancellationToken>()))
            .ReturnsAsync(count);
    }

    private Task<ReadingSessionStartedDto> StartAsync(int storyId = LibraryStory) =>
        _sut.StartAsync(ReadingHarness.Child, ReadingHarness.SessionKey, new StartReadingRequestDto { StoryId = storyId });

    private Task<ReadingSessionStartedDto> StartWithPlanAsync(int storyId = LibraryStory)
    {
        var service = CreateService(
            new PlanBenefitService(_plans, new PlanSettingsProvider(_plans), new ManualTimeProvider(Now)),
            new PlanUsageService(_h.UnitOfWork.Object));
        return service.StartAsync(ReadingHarness.Child, ReadingHarness.SessionKey, new StartReadingRequestDto { StoryId = storyId });
    }

    private void AddCycle(DateTime start, DateTime end, SubscriptionStatus status = SubscriptionStatus.Active,
        PlanType type = PlanType.Reading)
    {
        var id = _plans.Items<UserSubscription>().Count + 1;
        var plan = _plans.Seed(new SubscriptionPlan { Id = id, PlanType = type, MaxChildProfiles = 4 });
        _plans.Seed(new PaymentTransaction
        {
            Id = id, PlanId = plan.Id, PayerUserId = OwnerId,
            NewLibraryStoriesLimit = type == PlanType.Reading ? 24 : null
        });
        _plans.Seed(new UserSubscription
        {
            Id = id, UserId = OwnerId, PlanId = plan.Id, Plan = plan, PaymentId = id,
            StartAt = start, EndAt = end, Status = status
        });
    }

    private void AddFirstOpens(int count, DateTime startedAt, int firstStoryId = 1000)
    {
        for (var offset = 0; offset < count; offset++)
        {
            var storyId = firstStoryId + offset;
            if (storyId != LibraryStory)
                _h.AddLibraryStory(storyId, "Truyện đã mở");
            _h.Sessions.Add(new ReadingSession
            {
                Id = _h.Sessions.Count + 1, ChildProfileId = ReadingHarness.Child, StoryId = storyId,
                Status = SessionStatus.Completed, StartedAt = startedAt
            });
        }
    }

    [Fact]
    public async Task StartReading_CycleLimit_TwentyFourthAllowed_TwentyFifthForbidden()
    {
        AddCycle(CycleStart, CycleEnd);
        AddFirstOpens(23, CycleStart);
        const int nextStory = 31;
        _h.AddLibraryStory(nextStory, "Truyện tiếp theo");
        _h.Versions.Add(new StoryVersion { Id = nextStory * 10, StoryId = nextStory, VersionNo = 1, IsCurrent = true });

        var opened = await StartWithPlanAsync();
        var session = Assert.Single(_h.Sessions, s => s.Id == opened.SessionId);
        session.StartedAt = Now;

        var error = await Assert.ThrowsAsync<ForbiddenException>(() => StartWithPlanAsync(nextStory));
        Assert.Equal(string.Format(ChildReadingService.CycleLibraryLimitMessageFormat, 24, VietnamTime.LocalDate(CycleEnd)), error.Message);
        Assert.Equal(24, _h.Sessions.Count);
    }

    [Fact]
    public async Task StartReading_CycleLimitReached_MessageUsesVietnamDateOfCycleEnd()
    {
        AddCycle(CycleStart, CycleEnd);
        AddFirstOpens(24, CycleStart);

        var error = await Assert.ThrowsAsync<ForbiddenException>(() => StartWithPlanAsync());

        Assert.Contains("16/12/2026", error.Message);
    }

    [Fact]
    public async Task StartReading_CycleLimitReached_ReopenAlreadyOpenedStory_Allowed()
    {
        AddCycle(CycleStart, CycleEnd);
        AddFirstOpens(23, CycleStart);
        AddFirstOpens(1, CycleStart, LibraryStory);

        await StartWithPlanAsync();

        Assert.Equal(25, _h.Sessions.Count);
    }

    [Fact]
    public async Task StartReading_CycleLimitReached_EventExemptStory_Allowed()
    {
        AddCycle(CycleStart, CycleEnd);
        AddFirstOpens(24, CycleStart);
        _eventHooks.Setup(h => h.IsStoryExemptFromWeeklyLimitAsync(OwnerId, LibraryStory, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await StartWithPlanAsync();

        var waiver = Assert.Single(_h.Telemetry);
        Assert.Equal(result.SessionId, waiver.ReadingSessionId);
        Assert.Equal("{\"eventWeeklyLimitWaived\":true}", waiver.EventPayload);
    }

    [Fact]
    public async Task StartReading_NewCycleStarted_CountResets()
    {
        AddCycle(CycleStart.AddMonths(-1), CycleStart, SubscriptionStatus.Expired);
        AddFirstOpens(24, CycleStart.AddMinutes(-1));
        AddCycle(CycleStart, CycleEnd);

        await StartWithPlanAsync();

        Assert.Equal(25, _h.Sessions.Count);
    }

    [Fact]
    public async Task StartReading_UpgradedToCreativeAfterLimit_Allowed()
    {
        AddCycle(CycleStart, Now, SubscriptionStatus.Superseded);
        AddFirstOpens(24, CycleStart);
        AddCycle(Now, Now.AddMonths(1), type: PlanType.Creative);

        await StartWithPlanAsync();

        Assert.Equal(25, _h.Sessions.Count);
    }

    [Fact]
    public async Task StartReading_FreeWeeklyLimit_Unchanged()
    {
        AddFirstOpens(3, VietnamTime.StartOfWeekUtc(Now));

        var error = await Assert.ThrowsAsync<ForbiddenException>(() => StartWithPlanAsync());

        Assert.Equal(string.Format(ChildReadingService.WeeklyLibraryLimitMessageFormat, 3), error.Message);
    }

    [Fact]
    public async Task StartAsync_FreeFourthNewLibraryStoryInWeek_ThrowsForbidden()
    {
        var error = await Assert.ThrowsAsync<ForbiddenException>(() => StartAsync());

        Assert.Contains("3 truyện", error.Message);
        Assert.Contains("thứ Hai", error.Message);
        Assert.Empty(_h.Sessions);
    }

    [Fact]
    public async Task StartAsync_ThirdNewLibraryStory_Allowed()
    {
        NewThisWeek(2);

        await StartAsync();

        Assert.Single(_h.Sessions);
    }

    [Fact]
    public async Task StartAsync_ReopeningAStoryAnotherChildOpened_IsNotCounted()
    {
        _usage.Setup(u => u.HasOpenedLibraryStoryAsync(OwnerId, LibraryStory, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await StartAsync();

        Assert.Single(_h.Sessions);
    }

    [Fact]
    public async Task StartAsync_ParentStory_NotLimited()
    {
        await StartAsync(ReadingHarness.Story);

        Assert.Single(_h.Sessions);
    }

    [Fact]
    public async Task StartAsync_ResumingOpenSession_Allowed()
    {
        _h.Sessions.Add(new ReadingSession
        {
            Id = 1, ChildProfileId = ReadingHarness.Child, StoryId = LibraryStory, StoryVersionId = LibraryVersion,
            Status = SessionStatus.Reading, StartedAt = DateTime.UtcNow.AddMinutes(-5)
        });
        _usage.Setup(u => u.HasOpenedLibraryStoryAsync(OwnerId, LibraryStory, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await StartAsync();

        Assert.True(result.Resumed);
    }

    [Fact]
    public async Task StartAsync_PaidPlan_Unlimited()
    {
        WeeklyLimit(null);
        NewThisWeek(50);

        await StartAsync();

        Assert.Single(_h.Sessions);
    }

    [Fact]
    public async Task StartAsync_LibraryStory_LocksTheAccountBeforeCounting()
    {
        NewThisWeek(0);

        await StartAsync();

        _h.UnitOfWork.Verify(u => u.AcquireTransactionLockAsync(-(1_000_000_000 + OwnerId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartReading_EventExemptionIsRecordedOnSessionStart()
    {
        _eventHooks
            .Setup(h => h.IsStoryExemptFromWeeklyLimitAsync(OwnerId, LibraryStory, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await StartAsync();

        var log = Assert.Single(_h.Telemetry);
        Assert.Equal(result.SessionId, log.ReadingSessionId);
        Assert.Equal("{\"eventWeeklyLimitWaived\":true}", log.EventPayload);
        _usage.Verify(u => u.CountNewLibraryStoriesInPeriodAsync(
            OwnerId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartReading_UnlimitedPlan_DoesNotRecordEventWaiver()
    {
        WeeklyLimit(null);
        _eventHooks
            .Setup(h => h.IsStoryExemptFromWeeklyLimitAsync(OwnerId, LibraryStory, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await StartAsync();

        Assert.DoesNotContain(_h.Telemetry, t => t.EventPayload != null && t.EventPayload.Contains("eventWeeklyLimitWaived"));
    }
}
