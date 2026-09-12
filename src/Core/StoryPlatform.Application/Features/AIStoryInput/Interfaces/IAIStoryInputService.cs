using StoryPlatform.Application.Features.AIStoryInput.DTOs;

namespace StoryPlatform.Application.Features.AIStoryInput.Interfaces;

public interface IAIStoryInputService
{
    Task<AIStoryInputContextDto> GetContextAsync(int userId, int childProfileId, CancellationToken cancellationToken = default);

    Task<AIStoryInputProgressDto> SubmitAsync(int userId, SubmitAIStoryInputRequestDto request, CancellationToken cancellationToken = default);

    Task<AIStoryInputProgressDto> GetProgressAsync(int userId, int storyId, int requestId, CancellationToken cancellationToken = default);

    Task<AIStoryInputProgressDto> RetryAsync(int userId, int storyId, int requestId, RetryAIStoryInputRequestDto request, CancellationToken cancellationToken = default);
}
