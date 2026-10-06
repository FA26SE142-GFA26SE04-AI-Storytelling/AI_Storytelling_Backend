using System.Collections.Concurrent;
using StoryPlatform.Application.Abstractions.Security;

namespace StoryPlatform.Infrastructure.Security;

/// <summary>
/// Bộ đếm trong bộ nhớ của một instance: 5 lần sai liên tiếp khóa 5 phút.
/// Chuỗi sai cũ hơn 5 phút kể từ lần sai gần nhất không được cộng dồn.
/// </summary>
public class InMemoryClientAttemptLimiter : IClientAttemptLimiter
{
    public const int MaxFailures = 5;
    public static readonly TimeSpan BlockDuration = TimeSpan.FromMinutes(5);

    private const int PruneThreshold = 10_000;

    private sealed class State
    {
        public int Failures;
        public DateTimeOffset LastFailureAt;
        public DateTimeOffset? BlockedUntil;
    }

    private readonly ConcurrentDictionary<string, State> _states = new();
    private readonly TimeProvider _timeProvider;

    public InMemoryClientAttemptLimiter(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public bool IsBlocked(string clientKey)
    {
        if (!_states.TryGetValue(clientKey, out var state))
        {
            return false;
        }

        var now = _timeProvider.GetUtcNow();
        lock (state)
        {
            if (state.BlockedUntil is { } until && until > now)
            {
                return true;
            }

            if (IsStale(state, now))
            {
                _states.TryRemove(clientKey, out _);
            }

            return false;
        }
    }

    public void RegisterFailure(string clientKey)
    {
        var now = _timeProvider.GetUtcNow();
        var state = _states.GetOrAdd(clientKey, _ => new State());
        lock (state)
        {
            if (IsStale(state, now))
            {
                state.Failures = 0;
                state.BlockedUntil = null;
            }

            state.Failures++;
            state.LastFailureAt = now;
            if (state.Failures >= MaxFailures)
            {
                state.BlockedUntil = now + BlockDuration;
            }
        }

        if (_states.Count > PruneThreshold)
        {
            Prune(now);
        }
    }

    public void Reset(string clientKey) => _states.TryRemove(clientKey, out _);

    private static bool IsStale(State state, DateTimeOffset now) =>
        state.BlockedUntil is { } until
            ? until <= now
            : now - state.LastFailureAt >= BlockDuration;

    private void Prune(DateTimeOffset now)
    {
        foreach (var pair in _states)
        {
            if (IsStale(pair.Value, now))
            {
                _states.TryRemove(pair.Key, out _);
            }
        }
    }
}
