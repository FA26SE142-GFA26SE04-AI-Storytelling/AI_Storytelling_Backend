using StoryPlatform.Application.Features.Outline.DTOs;

namespace StoryPlatform.Application.Features.Outline.Interfaces;

public interface IOutlineService
{
    Task<OutlineProgressDto> GetCurrentAsync(int userId, int storyId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OutlineVersionDto>> GetVersionsAsync(int userId, int storyId, CancellationToken cancellationToken = default);
    Task<OutlineVersionDto> GetVersionAsync(int userId, int storyId, int versionNo, CancellationToken cancellationToken = default);
    Task<OutlineVersionDto> EditAsync(int userId, int storyId, int versionNo, EditOutlineRequestDto input, CancellationToken cancellationToken = default);
    Task<OutlineProgressDto> RegenerateAsync(int userId, int storyId, int versionNo, RegenerateOutlineRequestDto input, CancellationToken cancellationToken = default);
    Task<OutlineProgressDto> RetryInitialAsync(int userId, int storyId, RetryOutlineRequestDto input, CancellationToken cancellationToken = default);
    Task<OutlineProgressDto> ApproveAsync(int userId, int storyId, int versionNo, ApproveOutlineRequestDto input, CancellationToken cancellationToken = default);
    Task<OutlineProgressDto> RejectAsync(int userId, int storyId, int versionNo, RejectOutlineRequestDto input, CancellationToken cancellationToken = default);
}

public interface IOutlineJobProcessor
{
    Task<bool> ProcessNextAsync(CancellationToken cancellationToken = default);
}

public interface IOutlineJobFailureFinalizer
{
    Task MarkFailedAsync(
        int jobId,
        string expectedConcurrencyToken,
        string errorCode,
        CancellationToken cancellationToken = default);
}
