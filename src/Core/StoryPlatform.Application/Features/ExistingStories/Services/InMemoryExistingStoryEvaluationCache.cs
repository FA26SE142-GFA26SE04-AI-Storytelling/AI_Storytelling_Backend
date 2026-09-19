using System.Collections.Concurrent;
using StoryPlatform.Application.Features.ExistingStories.DTOs;
using StoryPlatform.Application.Features.ExistingStories.Interfaces;

namespace StoryPlatform.Application.Features.ExistingStories.Services;

public sealed class InMemoryExistingStoryEvaluationCache : IExistingStoryEvaluationCache
{
    private readonly ConcurrentDictionary<(int StoryId, int VersionId), CachedEvaluation> _cache = new();
    private static readonly TimeSpan Expiration = TimeSpan.FromMinutes(15);

    public ExistingStoryEvaluationDto? Get(int storyId, int storyVersionId)
    {
        var key = (storyId, storyVersionId);
        if (!_cache.TryGetValue(key, out var cached)) return null;
        if (cached.ExpiresAt > DateTime.UtcNow) return cached.Evaluation;

        _cache.TryRemove(key, out _);
        return null;
    }

    public void Set(ExistingStoryEvaluationDto evaluation) =>
        _cache[(evaluation.StoryId, evaluation.StoryVersionId)] =
            new CachedEvaluation(evaluation, DateTime.UtcNow.Add(Expiration));

    private sealed record CachedEvaluation(ExistingStoryEvaluationDto Evaluation, DateTime ExpiresAt);
}
