using System.Collections.Concurrent;
using StoryPlatform.Application.Features.StoryReview.Interfaces;

namespace StoryPlatform.Application.Features.StoryReview.Services;

public sealed class InMemoryReviewCompletionStore : IReviewCompletionStore
{
    private sealed class VersionReviewState
    {
        public bool StoryReviewed { get; set; }
        public bool VocabularyReviewed { get; set; }
        public bool QuizReviewed { get; set; }
        public bool DiscussionReviewed { get; set; }
    }

    private readonly ConcurrentDictionary<string, VersionReviewState> _states = new();

    private static string Key(int storyId, int versionId) => $"{storyId}:{versionId}";

    private VersionReviewState GetOrCreate(int storyId, int versionId) =>
        _states.GetOrAdd(Key(storyId, versionId), _ => new VersionReviewState());

    public void MarkStoryReviewed(int storyId, int versionId, int userId)
    {
        GetOrCreate(storyId, versionId).StoryReviewed = true;
    }

    public void MarkVocabularyReviewed(int storyId, int versionId, int userId)
    {
        GetOrCreate(storyId, versionId).VocabularyReviewed = true;
    }

    public void MarkQuizReviewed(int storyId, int versionId, int userId)
    {
        GetOrCreate(storyId, versionId).QuizReviewed = true;
    }

    public void MarkDiscussionReviewed(int storyId, int versionId, int userId)
    {
        GetOrCreate(storyId, versionId).DiscussionReviewed = true;
    }

    public bool IsStoryReviewed(int storyId, int versionId) =>
        _states.TryGetValue(Key(storyId, versionId), out var s) && s.StoryReviewed;

    public bool IsVocabularyReviewed(int storyId, int versionId) =>
        _states.TryGetValue(Key(storyId, versionId), out var s) && s.VocabularyReviewed;

    public bool IsQuizReviewed(int storyId, int versionId) =>
        _states.TryGetValue(Key(storyId, versionId), out var s) && s.QuizReviewed;

    public bool IsDiscussionReviewed(int storyId, int versionId) =>
        _states.TryGetValue(Key(storyId, versionId), out var s) && s.DiscussionReviewed;

    public bool IsReviewCompleted(int storyId, int versionId)
    {
        return _states.TryGetValue(Key(storyId, versionId), out var s) &&
               s.StoryReviewed && s.VocabularyReviewed && s.QuizReviewed && s.DiscussionReviewed;
    }

    public void Reset(int storyId, int versionId)
    {
        _states.TryRemove(Key(storyId, versionId), out _);
    }
}
