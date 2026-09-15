using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Contracts.AI.Responses;

namespace StoryPlatform.Application.Abstractions.AI;

public interface IAIStoryGenerationClient
{
    Task<GenerateOutlineResponse> GenerateOutlineAsync(GenerateOutlineRequest request, CancellationToken cancellationToken = default);
    Task<GenerateStoryResponse> GenerateStoryAsync(GenerateStoryRequest request, CancellationToken cancellationToken = default);
    Task<RefineStoryResponse> RefineStoryAsync(RefineStoryRequest request, CancellationToken cancellationToken = default);
    Task<EvaluateStoryResponse> EvaluateStoryAsync(EvaluateStoryRequest request, CancellationToken cancellationToken = default);
    Task<GenerateStoryContentResponse> GenerateStoryContentAsync(GenerateStoryContentRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
    Task<RefineStoryContentResponse> RefineStoryContentAsync(RefineStoryContentRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
    Task<GenerateVocabularyResponse> GenerateVocabularyAsync(GenerateVocabularyRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
    Task<GenerateQuizResponse> GenerateQuizAsync(GenerateQuizRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
    Task<GenerateDiscussionResponse> GenerateDiscussionAsync(GenerateDiscussionRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
    Task<EvaluateContentSafetyResponse> EvaluateContentSafetyAsync(EvaluateContentSafetyRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

public sealed class AIServiceRequestException : Exception
{
    public AIServiceRequestException(int statusCode, string errorCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }

    public int StatusCode { get; }
    public string ErrorCode { get; }
}
