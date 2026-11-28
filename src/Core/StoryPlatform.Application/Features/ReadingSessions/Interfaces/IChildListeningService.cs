namespace StoryPlatform.Application.Features.ReadingSessions.Interfaces;

public interface IChildListeningService
{
    Task SaveStepsAsync(int childProfileId, int sessionId, int interactionId,
        ListeningStepsRequestDto request, CancellationToken ct = default);
    Task<ListeningAttemptResultDto> SubmitAttemptAsync(int childProfileId, int sessionId, int interactionId,
        SubmitListeningAttemptRequestDto request, CancellationToken ct = default);
}
