namespace StoryPlatform.Application.Features.ReadingSessions.Interfaces;

public interface IChildQuizService
{
    Task<QuizDto> GetQuizAsync(int childProfileId, int sessionId, CancellationToken ct = default);
    Task<QuizAnswerResultDto> SubmitAnswerAsync(int childProfileId, int sessionId, SubmitQuizAnswerRequestDto request, CancellationToken ct = default);
    Task<QuizResultDto> GetResultAsync(int childProfileId, int sessionId, CancellationToken ct = default);
}
