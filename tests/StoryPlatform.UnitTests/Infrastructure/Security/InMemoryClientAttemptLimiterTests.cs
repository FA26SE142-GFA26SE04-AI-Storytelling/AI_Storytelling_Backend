using StoryPlatform.Infrastructure.Security;
using Xunit;

namespace StoryPlatform.UnitTests.Infrastructure.Security;

public class InMemoryClientAttemptLimiterTests
{
    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 6, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private readonly TestClock _clock = new();
    private readonly InMemoryClientAttemptLimiter _sut;

    public InMemoryClientAttemptLimiterTests()
    {
        _sut = new InMemoryClientAttemptLimiter(_clock);
    }

    private void Fail(string clientKey, int times)
    {
        for (var i = 0; i < times; i++)
        {
            _sut.RegisterFailure(clientKey);
        }
    }

    [Fact]
    public void IsBlocked_UnknownClient_ReturnsFalse() =>
        Assert.False(_sut.IsBlocked("203.0.113.7"));

    [Fact]
    public void RegisterFailure_FourFailures_DoesNotBlock()
    {
        Fail("203.0.113.7", InMemoryClientAttemptLimiter.MaxFailures - 1);
        Assert.False(_sut.IsBlocked("203.0.113.7"));
    }

    [Fact]
    public void RegisterFailure_FifthFailure_BlocksClient()
    {
        Fail("203.0.113.7", InMemoryClientAttemptLimiter.MaxFailures);
        Assert.True(_sut.IsBlocked("203.0.113.7"));
    }

    [Fact]
    public void IsBlocked_AfterBlockDuration_ReturnsFalse()
    {
        Fail("203.0.113.7", InMemoryClientAttemptLimiter.MaxFailures);
        _clock.Now += InMemoryClientAttemptLimiter.BlockDuration;
        Assert.False(_sut.IsBlocked("203.0.113.7"));
    }

    [Fact]
    public void IsBlocked_JustBeforeBlockDurationEnds_StillBlocked()
    {
        Fail("203.0.113.7", InMemoryClientAttemptLimiter.MaxFailures);
        _clock.Now += InMemoryClientAttemptLimiter.BlockDuration - TimeSpan.FromSeconds(1);
        Assert.True(_sut.IsBlocked("203.0.113.7"));
    }

    [Fact]
    public void Reset_ClearsFailureCount()
    {
        Fail("203.0.113.7", InMemoryClientAttemptLimiter.MaxFailures - 1);
        _sut.Reset("203.0.113.7");
        Fail("203.0.113.7", InMemoryClientAttemptLimiter.MaxFailures - 1);
        Assert.False(_sut.IsBlocked("203.0.113.7"));
    }

    [Fact]
    public void RegisterFailure_ClientsAreIndependent()
    {
        Fail("203.0.113.7", InMemoryClientAttemptLimiter.MaxFailures);
        Assert.True(_sut.IsBlocked("203.0.113.7"));
        Assert.False(_sut.IsBlocked("198.51.100.9"));
    }

    [Fact]
    public void RegisterFailure_FailuresOlderThanWindow_DoNotAccumulate()
    {
        Fail("203.0.113.7", InMemoryClientAttemptLimiter.MaxFailures - 1);
        _clock.Now += InMemoryClientAttemptLimiter.BlockDuration;
        _sut.RegisterFailure("203.0.113.7");
        Assert.False(_sut.IsBlocked("203.0.113.7"));
    }

    [Fact]
    public void RegisterFailure_AfterBlockExpired_StartsNewCount()
    {
        Fail("203.0.113.7", InMemoryClientAttemptLimiter.MaxFailures);
        _clock.Now += InMemoryClientAttemptLimiter.BlockDuration;
        Fail("203.0.113.7", InMemoryClientAttemptLimiter.MaxFailures - 1);
        Assert.False(_sut.IsBlocked("203.0.113.7"));
    }
}
