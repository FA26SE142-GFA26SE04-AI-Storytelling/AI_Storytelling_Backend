namespace StoryPlatform.Application.Features.StoryReview.Interfaces;

/// <summary>
/// Quản lý trạng thái hoàn tất review của các cấu phần (Story, Vocabulary, Quiz, Discussion)
/// cho từng StoryVersion cụ thể trong bộ nhớ mà không thay đổi database migration.
/// </summary>
public interface IReviewCompletionStore
{
    void MarkStoryReviewed(int storyId, int versionId, int userId);
    void MarkVocabularyReviewed(int storyId, int versionId, int userId);
    void MarkQuizReviewed(int storyId, int versionId, int userId);
    void MarkDiscussionReviewed(int storyId, int versionId, int userId);

    bool IsStoryReviewed(int storyId, int versionId);
    bool IsVocabularyReviewed(int storyId, int versionId);
    bool IsQuizReviewed(int storyId, int versionId);
    bool IsDiscussionReviewed(int storyId, int versionId);

    bool IsReviewCompleted(int storyId, int versionId);
    void Reset(int storyId, int versionId);
}
