using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildRoadmaps;
using StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;
using StoryPlatform.Application.Features.Events.Interfaces;
using StoryPlatform.Application.Features.PlanBenefits.DTOs;
using StoryPlatform.Application.Features.PlanBenefits.Interfaces;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using StoryPlatform.Application.Features.ReadingSessions.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ReadingSessions;

/// <summary>F-17: mức Miễn phí mở tối đa N truyện thư viện mới mỗi tuần, tính theo tài khoản (Luồng 2, 2h; Luồng 4).</summary>
public sealed class ChildReadingLibraryLimitTests
{
    private const int OwnerId = 7;
    private const int LibraryStory = 30;
    private const int LibraryVersion = 300;

    private readonly ReadingHarness _h = new();
    private readonly Mock<IPlanBenefitService> _benefits = new();
    private readonly Mock<IPlanUsageService> _usage = new();
    private readonly Mock<IEventIntegrationHooks> _eventHooks = new();
    private readonly ChildReadingService _sut;

    public ChildReadingLibraryLimitTests()
    {
        var eligibility = new Mock<IChildRoadmapEligibility>();
        eligibility.Setup(e => e.EvaluateAsync(It.IsAny<ChildProfile>(), It.IsAny<Story?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChildRoadmapSkipReason?)null);
        _h.AddLibraryStory(LibraryStory, "Truyện thư viện");
        _h.Versions.Add(new StoryVersion { Id = LibraryVersion, StoryId = LibraryStory, VersionNo = 1, Title = "Truyện thư viện", IsCurrent = true });
        WeeklyLimit(3);
        NewThisWeek(3);
        _usage.Setup(u => u.HasOpenedLibraryStoryAsync(OwnerId, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _eventHooks.Setup(h => h.IsStoryExemptFromWeeklyLimitAsync(OwnerId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _sut = new ChildReadingService(_h.UnitOfWork.Object, eligibility.Object,
            new Mock<IChildRoadmapProgressRecorder>().Object, new Mock<IChildRewardService>().Object,
            planBenefits: _benefits.Object, planUsage: _usage.Object, eventHooks: _eventHooks.Object);
    }

    private void WeeklyLimit(int? limit) => _benefits
        .Setup(b => b.GetForUserAsync(OwnerId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new PlanBenefitsDto { MaxChildProfiles = 4, WeeklyNewLibraryStories = limit });

    private void NewThisWeek(int count)
    {
        _usage
            .Setup(u => u.CountNewLibraryStoriesThisWeekAsync(OwnerId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(count);
    }

    private Task<ReadingSessionStartedDto> StartAsync(int storyId = LibraryStory) =>
        _sut.StartAsync(ReadingHarness.Child, ReadingHarness.SessionKey, new StartReadingRequestDto { StoryId = storyId });

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
        _usage.Verify(u => u.CountNewLibraryStoriesThisWeekAsync(OwnerId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
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
