using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Application.Features.AIStoryInput.Models;

namespace StoryPlatform.Application.Features.ContentGeneration.Quality;

public sealed record ContentQualityGate(bool Passed, bool CanRefine, string? ReasonCode, IReadOnlyList<string> Violations);

public sealed record ContentQualityResult(
    bool IsPassed,
    ContentQualityGate OutlineConsistency,
    ContentQualityGate Length,
    ContentQualityGate Safety,
    ContentQualityGate Readability,
    ContentQualityGate VocabularyCompliance)
{
    public IReadOnlyList<string> RefinementReasons =>
        new[] { OutlineConsistency, Length, Safety, Readability, VocabularyCompliance }
            .Where(item => !item.Passed && item.CanRefine)
            .SelectMany(item => item.Violations)
            .ToArray();
}

public interface IContentQualityEvaluator
{
    ContentQualityResult Evaluate(
        StoryContentDto story,
        StoryOutlineDto approvedOutline,
        AcceptedAIStoryInputSnapshot input,
        AIStoryInputContextSnapshot context);
}
