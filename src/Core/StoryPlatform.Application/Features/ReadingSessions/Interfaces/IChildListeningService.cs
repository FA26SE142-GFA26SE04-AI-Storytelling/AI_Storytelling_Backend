namespace StoryPlatform.Application.Features.ReadingSessions.Interfaces;

public interface IChildListeningService
{
    Task<ListeningAttemptResultDto> SubmitAttemptAsync(int childProfileId, int sessionId, int interactionId,
        SubmitListeningAttemptRequestDto request, CancellationToken ct = default);
}
