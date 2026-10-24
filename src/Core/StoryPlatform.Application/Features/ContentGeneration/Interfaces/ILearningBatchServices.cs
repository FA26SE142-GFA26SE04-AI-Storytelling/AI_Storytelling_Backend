using StoryPlatform.Application.Features.AIStoryInput.Models;
using StoryPlatform.Contracts.AI.Responses;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.ContentGeneration.Interfaces;

public sealed record LearningBatchRequest(GenerateVocabularyRequest? Vocabulary, GenerateQuizRequest? Quiz,
    AIStoryInputContextSnapshot Context);
public sealed record LearningBatchPackage(GenerateVocabularyResponse Vocabulary, GenerateQuizResponse Quiz);

public interface ILearningBatchRunner
{
    Task RunAsync(IReadOnlyList<int> batchIds, string rootClaimToken, CancellationToken ct);
}
public interface ILearningBatchProcessor
{
    Task ProcessAsync(int batchId, string rootClaimToken, CancellationToken ct);
}
public interface ILearningRequestGate
{
    Task<IDisposable> EnterAsync(CancellationToken ct);
}
public interface ILearningBatchService
{
    Task<LearningBatchPackage> GenerateAsync(StoryGenerationJob root, string token, StoryVersion version,
        AIStoryInputContextSnapshot context, CancellationToken ct);
    Task<GenerateQuizResponse> ReadQuizAsync(int storyId, int versionId, CancellationToken ct);
    Task ValidateSourceAsync(int rootJobId, Story story, StoryVersion version, int actor, CancellationToken ct);
}
