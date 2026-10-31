namespace StoryPlatform.Application.Features.ReadingSessions.Interfaces;

public interface IChildReadAloudService
{
    Task<ReadAloudResultDto> CheckAsync(
        int childProfileId, int sessionId, int segmentId, ReadAloudRequest request, CancellationToken ct = default);
}
