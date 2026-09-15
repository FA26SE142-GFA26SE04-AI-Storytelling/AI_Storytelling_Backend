using StoryPlatform.Application.Features.ContentGeneration.DTOs;

namespace StoryPlatform.Application.Features.ContentGeneration.Interfaces;

public interface IContentGenerationService
{
    Task<ContentGenerationProgressDto> GetProgressAsync(int userId, int storyId, CancellationToken cancellationToken = default);
}

public interface IContentGenerationJobProcessor
{
    Task<bool> ProcessNextAsync(CancellationToken cancellationToken = default);
}

public interface IContentGenerationJobFailureFinalizer
{
    Task MarkFailedAsync(int jobId, string expectedConcurrencyToken, string errorCode, CancellationToken cancellationToken = default);
}
