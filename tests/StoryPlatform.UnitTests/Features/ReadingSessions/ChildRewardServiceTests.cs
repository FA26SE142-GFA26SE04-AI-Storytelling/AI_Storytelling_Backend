using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.ReadingSessions.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ReadingSessions;

public sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public ManualTimeProvider(DateTimeOffset now)
    {
        _now = now;
    }

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

public class ChildRewardServiceTests
{
    // 10:00 giờ Việt Nam ngày 2026-10-22 (= 03:00 UTC).
    private static readonly DateTimeOffset Noon = new(2026, 10, 22, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 22);

    private readonly ReadingHarness _h = new();
    private readonly ManualTimeProvider _clock = new(Noon);
    private readonly ChildRewardService _sut;

    public ChildRewardServiceTests()
    {
        _sut = new ChildRewardService(_h.UnitOfWork.Object, _clock);
    }

    private ReadingSession Completed(int storyId = ReadingHarness.Story) =>
        _h.AddSession(SessionStatus.Completed, storyId: storyId);

    private Achievement Row() => _h.Achievements.Single(a => a.ChildProfileId == ReadingHarness.Child);

    // ---- OnStoryCompletedAsync ----

    [Fact]
    public async Task FirstCompletion_GivesTenExp_StartsStreak_AndAwardsFirstStoryBadge()
    {
        var session = Completed();

        var outcome = await _sut.OnStoryCompletedAsync(session, isRetake: false);

        Assert.Equal(10, outcome.ExpGained);
        Assert.Equal(new[] { BadgeCatalog.FirstStory }, outcome.NewBadgeCodes);
        Assert.Equal(10, Row().ExpTotal);
        Assert.Equal(1, Row().StreakDays);
        Assert.Equal(Today, Row().LastStreakDate);
        Assert.Contains(_h.Badges, b => b.BadgeCode == BadgeCatalog.FirstStory && b.ChildProfileId == ReadingHarness.Child);
        Assert.Contains(_h.Telemetry, t => t.EventType == TelemetryEventType.RewardGranted && t.ReadingSessionId == session.Id);
        Assert.Contains(_h.Telemetry, t => t.EventType == TelemetryEventType.BadgeUnlocked);
    }

    [Fact]
    public async Task Retake_GivesNoExp_ButStillCountsTheDayAndNeverRepeatsABadge()
    {
        await _sut.OnStoryCompletedAsync(Completed(), isRetake: false);
        _clock.Advance(TimeSpan.FromDays(1));

        var outcome = await _sut.OnStoryCompletedAsync(Completed(), isRetake: true);

        Assert.Equal(0, outcome.ExpGained);
        Assert.Empty(outcome.NewBadgeCodes);
        Assert.Equal(10, Row().ExpTotal);
        Assert.Equal(2, Row().StreakDays);
        Assert.Single(_h.Badges, b => b.BadgeCode == BadgeCatalog.FirstStory);
        Assert.Single(_h.Telemetry, t => t.EventType == TelemetryEventType.RewardGranted);
    }

    [Fact]
    public async Task SecondCompletionOnTheSameDay_DoesNotRaiseTheStreak()
    {
        await _sut.OnStoryCompletedAsync(Completed(), isRetake: false);
        _clock.Advance(TimeSpan.FromHours(2));

        await _sut.OnStoryCompletedAsync(Completed(), isRetake: false);

        Assert.Equal(1, Row().StreakDays);
        Assert.Equal(20, Row().ExpTotal);
    }

    [Fact]
    public async Task MissingADay_ResetsTheStreakToOne()
    {
        await _sut.OnStoryCompletedAsync(Completed(), isRetake: false);
        _clock.Advance(TimeSpan.FromDays(2));

        await _sut.OnStoryCompletedAsync(Completed(), isRetake: false);

        Assert.Equal(1, Row().StreakDays);
        Assert.Equal(Today.AddDays(2), Row().LastStreakDate);
    }

    [Fact]
    public async Task DayBoundaryFollowsVietnamTime_NotUtc()
    {
        await _sut.OnStoryCompletedAsync(Completed(), isRetake: false);   // 22/10 10:00 giờ VN
        _clock.Advance(TimeSpan.FromHours(15));                            // 23/10 01:00 giờ VN, vẫn 22/10 theo UTC

        await _sut.OnStoryCompletedAsync(Completed(), isRetake: false);

        Assert.Equal(2, Row().StreakDays);
        Assert.Equal(Today.AddDays(1), Row().LastStreakDate);
    }

    [Theory]
    [InlineData(2, BadgeCatalog.Streak3)]
    [InlineData(6, BadgeCatalog.Streak7)]
    public async Task ReachingAStreakThreshold_AwardsItsBadge(int streakBefore, string expectedBadge)
    {
        _h.Achievements.Add(new Achievement
        {
            Id = 1, ChildProfileId = ReadingHarness.Child, StreakDays = streakBefore, LastStreakDate = Today.AddDays(-1)
        });

        var outcome = await _sut.OnStoryCompletedAsync(Completed(), isRetake: false);

        Assert.Contains(expectedBadge, outcome.NewBadgeCodes);
        Assert.Equal(streakBefore + 1, Row().StreakDays);
    }

    [Fact]
    public async Task FiveDifferentCompletedStories_AwardBookworm_ButRepeatsOfOneStoryDoNot()
    {
        for (var storyId = 100; storyId < 103; storyId++)
        {
            _h.AddSession(SessionStatus.Completed, storyId: storyId);
        }

        _h.AddSession(SessionStatus.Completed, storyId: 100);   // đọc lại: vẫn 3 truyện khác nhau
        var fourth = await _sut.OnStoryCompletedAsync(Completed(), isRetake: false);   // truyện thứ 4
        Assert.DoesNotContain(BadgeCatalog.Bookworm, fourth.NewBadgeCodes);

        var fifth = await _sut.OnStoryCompletedAsync(Completed(storyId: 104), isRetake: false);   // truyện thứ 5

        Assert.Contains(BadgeCatalog.Bookworm, fifth.NewBadgeCodes);
    }

    [Fact]
    public async Task AnExistingBadge_IsNotAwardedTwice()
    {
        _h.Badges.Add(new Badge { Id = 1, ChildProfileId = ReadingHarness.Child, BadgeCode = BadgeCatalog.FirstStory, EarnedAt = DateTime.UtcNow });

        var outcome = await _sut.OnStoryCompletedAsync(Completed(), isRetake: false);

        Assert.Empty(outcome.NewBadgeCodes);
        Assert.Single(_h.Badges);
    }

    [Fact]
    public async Task Rewards_BelongOnlyToTheCompletingChild()
    {
        _h.Achievements.Add(new Achievement { Id = 1, ChildProfileId = ReadingHarness.OtherChild, ExpTotal = 50 });

        await _sut.OnStoryCompletedAsync(Completed(), isRetake: false);

        Assert.Equal(50, _h.Achievements.Single(a => a.ChildProfileId == ReadingHarness.OtherChild).ExpTotal);
        Assert.Equal(10, Row().ExpTotal);
    }
    // ---- OnQuizAnsweredAsync ----

    [Fact]
    public async Task QuizFirstTimeCorrect_GivesTwoExp()
    {
        var session = Completed();

        var outcome = await _sut.OnQuizAnsweredAsync(session, firstTimeCorrect: true, perfectRunJustCompleted: false);

        Assert.Equal(2, outcome.ExpGained);
        Assert.Empty(outcome.NewBadgeCodes);
        Assert.Equal(2, Row().ExpTotal);
        Assert.Contains(_h.Telemetry, t => t.EventType == TelemetryEventType.RewardGranted);
    }

    [Fact]
    public async Task QuizNothingToReward_ChangesNothingAndCreatesNothing()
    {
        var session = Completed();

        var outcome = await _sut.OnQuizAnsweredAsync(session, firstTimeCorrect: false, perfectRunJustCompleted: false);

        Assert.Same(RewardOutcome.None, outcome);
        Assert.Empty(_h.Achievements);
        Assert.Empty(_h.Badges);
        Assert.Empty(_h.Telemetry);
    }

    [Fact]
    public async Task QuizPerfectRun_AwardsChampionOnce_EvenWithoutNewExp()
    {
        var session = Completed();

        var first = await _sut.OnQuizAnsweredAsync(session, firstTimeCorrect: false, perfectRunJustCompleted: true);
        var second = await _sut.OnQuizAnsweredAsync(session, firstTimeCorrect: false, perfectRunJustCompleted: true);

        Assert.Equal(new[] { BadgeCatalog.QuizChampion }, first.NewBadgeCodes);
        Assert.Empty(second.NewBadgeCodes);
        Assert.Single(_h.Badges, b => b.BadgeCode == BadgeCatalog.QuizChampion);
    }

    [Fact]
    public async Task QuizExp_AddsToTheExistingTotal()
    {
        _h.Achievements.Add(new Achievement { Id = 1, ChildProfileId = ReadingHarness.Child, ExpTotal = 30, StreakDays = 4, LastStreakDate = Today });

        await _sut.OnQuizAnsweredAsync(Completed(), firstTimeCorrect: true, perfectRunJustCompleted: false);

        Assert.Equal(32, Row().ExpTotal);
        Assert.Equal(4, Row().StreakDays);
    }

    // ---- GetSummaryAsync ----

    [Fact]
    public async Task Summary_ForAChildWithNothing_IsZeroWithAllBadgesUnearned()
    {
        var summary = await _sut.GetSummaryAsync(ReadingHarness.Child);

        Assert.Equal(0, summary.ExpTotal);
        Assert.Equal(0, summary.StreakDays);
        Assert.Equal(BadgeCatalog.All.Select(b => b.Code), summary.Badges.Select(b => b.Code));
        Assert.All(summary.Badges, badge =>
        {
            Assert.False(badge.Earned);
            Assert.Null(badge.EarnedAt);
        });
    }

    [Theory]
    [InlineData(0, true)]    // hôm nay
    [InlineData(-1, true)]   // hôm qua: còn hiệu lực
    [InlineData(-2, false)]  // đã bỏ lỡ
    public async Task Summary_StreakIsOnlyShownWhileStillAlive(int daysAgo, bool alive)
    {
        _h.Achievements.Add(new Achievement
        {
            Id = 1, ChildProfileId = ReadingHarness.Child, ExpTotal = 40, StreakDays = 5, LastStreakDate = Today.AddDays(daysAgo)
        });

        var summary = await _sut.GetSummaryAsync(ReadingHarness.Child);

        Assert.Equal(40, summary.ExpTotal);
        Assert.Equal(alive ? 5 : 0, summary.StreakDays);
    }

    [Fact]
    public async Task Summary_MarksEarnedBadges_AndIgnoresOtherChildrenAndUnknownCodes()
    {
        var earnedAt = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
        _h.Badges.Add(new Badge { Id = 1, ChildProfileId = ReadingHarness.Child, BadgeCode = BadgeCatalog.Bookworm, EarnedAt = earnedAt });
        _h.Badges.Add(new Badge { Id = 2, ChildProfileId = ReadingHarness.OtherChild, BadgeCode = BadgeCatalog.FirstStory, EarnedAt = earnedAt });
        _h.Badges.Add(new Badge { Id = 3, ChildProfileId = ReadingHarness.Child, BadgeCode = "FAST_READER", EarnedAt = earnedAt });

        var summary = await _sut.GetSummaryAsync(ReadingHarness.Child);

        var bookworm = summary.Badges.Single(b => b.Code == BadgeCatalog.Bookworm);
        Assert.True(bookworm.Earned);
        Assert.Equal(earnedAt, bookworm.EarnedAt);
        Assert.False(summary.Badges.Single(b => b.Code == BadgeCatalog.FirstStory).Earned);
        Assert.Equal(BadgeCatalog.All.Count, summary.Badges.Count);
    }
}
