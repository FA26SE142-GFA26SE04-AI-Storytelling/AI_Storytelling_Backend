namespace StoryPlatform.Application.Features.ReadingSessions.Interfaces;

public interface IChildStorySummaryService
{
    Task<StorySummarySentDto> SendRecordingAsync(
        int childProfileId, int sessionId, StorySummaryRecordingRequest request, CancellationToken ct = default);

    Task<StorySummaryQuestionSentDto> AskAsync(
        int childProfileId, int sessionId, StorySummaryQuestionRequest request, CancellationToken ct = default);
}
