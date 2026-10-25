namespace StoryPlatform.Application.Features.ReadingSessions.Interfaces;

public interface IChildReadingService
{
    Task<ReadingSessionStartedDto> StartAsync(
        int childProfileId, string sessionKey, StartReadingRequestDto request, CancellationToken ct = default);

    Task<ReadingContentDto> GetContentAsync(int childProfileId, int sessionId, CancellationToken ct = default);
    Task<ReadingProgressDto> SaveProgressAsync(int childProfileId, int sessionId, SaveProgressRequestDto request, CancellationToken ct = default);

    Task<ReadingCompletedDto> CompleteAsync(int childProfileId, int sessionId, CancellationToken ct = default);
    Task<ReadingFlagsDto> SetBookmarkAsync(int childProfileId, int sessionId, bool value, CancellationToken ct = default);
    Task<ReadingFlagsDto> SetFavoriteAsync(int childProfileId, int sessionId, bool value, CancellationToken ct = default);
    Task<ReadingFeelingDto> SetFeelingAsync(int childProfileId, int sessionId, SetFeelingRequestDto request, CancellationToken ct = default);

    Task<ForceExitSignalDto> GetSignalAsync(int childProfileId, int sessionId, CancellationToken ct = default);
    Task LogEventAsync(int childProfileId, int sessionId, ReadingEventRequestDto request, CancellationToken ct = default);
}
