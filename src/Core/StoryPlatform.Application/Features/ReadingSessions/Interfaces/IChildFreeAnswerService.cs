namespace StoryPlatform.Application.Features.ReadingSessions.Interfaces;

public interface IChildFreeAnswerService
{
    Task<bool> SaveAsync(int childProfileId, int sessionId, int interactionId,
        ReadAloudRequest request, CancellationToken ct = default);
}
