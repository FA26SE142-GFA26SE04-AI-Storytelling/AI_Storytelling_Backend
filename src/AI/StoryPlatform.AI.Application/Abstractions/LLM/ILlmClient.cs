using System.Text.Json;
using StoryPlatform.AI.Domain.Generation;

namespace StoryPlatform.AI.Application.Abstractions.LLM;

public interface ILlmClient
{
    Task<LlmGenerationResult> GenerateStructuredAsync(
        string prompt,
        string schemaName,
        JsonElement schema,
        CancellationToken cancellationToken = default);
}
