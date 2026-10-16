using System.Collections.Concurrent;

namespace StoryPlatform.Application.Features.CreatorAssistance.Services;

/// <summary>Best-effort cancellation on this process. Database state remains authoritative across instances.</summary>
public sealed class AssistanceCancellationRegistry
{
    private readonly ConcurrentDictionary<(int Task, string Operation), CancellationTokenSource> _sources = new();
    public CancellationTokenSource Register(int task, string operation)
    {
        var source = new CancellationTokenSource();
        if (!_sources.TryAdd((task, operation), source)) { source.Dispose(); throw new InvalidOperationException("Duplicate active operation."); }
        return source;
    }
    public void Cancel(int task, string operation)
    {
        if (_sources.TryGetValue((task, operation), out var source))
            try { source.Cancel(); } catch (ObjectDisposedException) { /* Completion won the race. */ }
    }
    public void Release(int task, string operation)
    {
        if (_sources.TryRemove((task, operation), out var source)) source.Dispose();
    }
}
