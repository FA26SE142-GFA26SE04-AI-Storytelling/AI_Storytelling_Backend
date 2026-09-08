using StoryPlatform.AI.Domain.Generation;
using StoryPlatform.Contracts.AI.Models;

namespace StoryPlatform.AI.Application.Common;

internal static class GenerationMapping
{
    public static GenerationMetadataDto ToMetadata(this LlmGenerationResult result, string promptVersion, int refinementCount = 0)
    {
        return new GenerationMetadataDto
        {
            ModelProvider = result.ModelProvider,
            Model = result.Model,
            ModelVersion = result.ModelVersion,
            PromptVersion = promptVersion,
            InputTokens = result.InputTokens,
            OutputTokens = result.OutputTokens,
            LatencyMs = result.LatencyMs,
            RefinementCount = refinementCount
        };
    }
}
