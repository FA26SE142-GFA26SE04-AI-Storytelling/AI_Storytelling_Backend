using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Domain.Entities;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ReadingSessions;

public sealed class RewardPolicyLiveStreakTests
{
    private static readonly DateOnly Today = new(2026, 11, 26);

    private static Achievement Streak(int days, DateOnly? last) =>
        new() { ChildProfileId = 1, StreakDays = days, LastStreakDate = last };

    [Fact]
    public void NoAchievement_IsZero() => Assert.Equal(0, RewardPolicy.LiveStreakDays(null, Today));

    [Fact]
    public void NeverRead_IsZero() => Assert.Equal(0, RewardPolicy.LiveStreakDays(Streak(0, null), Today));

    [Fact]
    public void ReadToday_KeepsStreak() => Assert.Equal(5, RewardPolicy.LiveStreakDays(Streak(5, Today), Today));

    [Fact]
    public void ReadYesterday_KeepsStreak() =>
        Assert.Equal(5, RewardPolicy.LiveStreakDays(Streak(5, Today.AddDays(-1)), Today));

    [Fact]
    public void LastReadTwoDaysAgo_StreakIsOver() =>
        Assert.Equal(0, RewardPolicy.LiveStreakDays(Streak(5, Today.AddDays(-2)), Today));
}
