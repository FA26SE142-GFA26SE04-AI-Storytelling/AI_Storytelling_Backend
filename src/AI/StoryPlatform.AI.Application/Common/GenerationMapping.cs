using StoryPlatform.AI.Domain.Generation;
using StoryPlatform.Contracts.AI.Models;

namespace StoryPlatform.AI.Application.Common;

internal static class GenerationMapping
{
    public static GenerationMetadataDto ToMetadata(
        this LlmGenerationResult result,
        string promptVersion,
        int refinementCount = 0,
        IReadOnlyList<GenerationAttemptMetadataDto>? attempts = null,
        IReadOnlyList<string>? promptVersions = null)
    {
        var resolvedAttempts = attempts ?? [result.ToAttempt("generation")];
        return new GenerationMetadataDto
        {
            ModelProvider = result.ModelProvider,
            Model = result.Model,
            ModelVersion = result.ModelVersion,
            PromptVersion = promptVersion,
            InputTokens = resolvedAttempts.Sum(item => item.InputTokens),
            OutputTokens = resolvedAttempts.Sum(item => item.OutputTokens),
            LatencyMs = resolvedAttempts.Sum(item => item.LatencyMs),
            RefinementCount = refinementCount,
            PromptVersions = promptVersions ?? [promptVersion],
            Attempts = resolvedAttempts
        };
    }

    public static GenerationAttemptMetadataDto ToAttempt(this LlmGenerationResult result, string operation) =>
        new()
        {
            Operation = operation,
            ModelProvider = result.ModelProvider,
            Model = result.Model,
            InputTokens = result.InputTokens,
            OutputTokens = result.OutputTokens,
            LatencyMs = result.LatencyMs
        };
}
