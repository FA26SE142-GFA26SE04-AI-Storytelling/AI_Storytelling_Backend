namespace StoryPlatform.Application.Features.ReadingSessions.Interfaces;

public interface IChildStorySummaryService
{
    Task<StorySummarySentDto> SendRecordingAsync(
        int childProfileId, int sessionId, StorySummaryRecordingRequest request, CancellationToken ct = default);

    Task<StorySummarySentDto> SendFlagsAsync(
        int childProfileId, int sessionId, StorySummaryFlagsRequestDto request, CancellationToken ct = default);
}
