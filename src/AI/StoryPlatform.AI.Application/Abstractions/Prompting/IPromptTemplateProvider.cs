using StoryPlatform.AI.Domain.Enums;
using StoryPlatform.Contracts.AI.Requests;

namespace StoryPlatform.AI.Application.Abstractions.Prompting;

public sealed record PromptTemplate(string Version, string Template);

public interface IPromptTemplateProvider
{
    /// <summary>
    /// Get the active prompt template with basic parameters.
    /// </summary>
    PromptTemplate GetActive(PromptType promptType, string language, string ageBand);

    /// <summary>
    /// Get the active prompt template with full profile-aware request context for outline generation.
    /// Returns enhanced, age-specific instructions for better story quality.
    /// </summary>
    PromptTemplate GetActiveForOutline(GenerateOutlineRequest request, PromptType promptType = PromptType.Outline);
}
