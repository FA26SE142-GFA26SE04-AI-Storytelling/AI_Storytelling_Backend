namespace StoryPlatform.Application.Features.ReadingSessions.Interfaces;

public interface IChildReadAloudService
{
    Task<ReadAloudResultDto> CheckAsync(
        int childProfileId, int sessionId, int segmentId, ReadAloudRequest request, CancellationToken ct = default);
    /// <summary>Chấm giọng bé theo đoạn luyện đọc to (ListeningInteraction loại ReadAloud) của phiên.</summary>
    Task<ReadAloudResultDto> CheckPracticeAsync(
        int childProfileId, int sessionId, int interactionId, ReadAloudRequest request, CancellationToken ct = default);
}
