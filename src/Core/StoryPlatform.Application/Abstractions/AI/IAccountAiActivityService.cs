using StoryPlatform.Domain.Enums;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Abstractions.AI;

public static class AiStoryConcurrencyLimits
{
    public const int MaxConcurrentStories = 2;
    public const string ErrorCode = "AI_STORY_CONCURRENCY_LIMIT";
    public const string ErrorMessage = "Bạn đang tạo 2 truyện. Hãy hoàn tất một truyện trước khi bắt đầu truyện khác.";
}

public sealed record ActiveAiStoryDto(
    int StoryId,
    string Title,
    string ActivityMode,
    string StoryStatus,
    DateTime? StartedAt);

public sealed record AiGenerationCapacityDto(
    int Limit,
    int ActiveCount,
    bool CanStartNewStory,
    IReadOnlyList<ActiveAiStoryDto> ActiveStories);

public interface IAccountAiActivityService
{
    Task<AiGenerationCapacityDto> GetCapacityAsync(int accountUserId, CancellationToken cancellationToken = default);
    Task EnsureCanAdmitNewStoryAsync(int accountUserId, CancellationToken cancellationToken = default);
    Task AdmitStoryAsync(int accountUserId, int storyId, AiActivityMode requestedMode, CancellationToken cancellationToken = default);
    Task AdmitStoryAsync(int accountUserId, Story story, AiActivityMode requestedMode, CancellationToken cancellationToken = default);
    Task ReconcileStoryAiActivityAsync(int accountUserId, int storyId, CancellationToken cancellationToken = default, int? failedJobId = null);
}
