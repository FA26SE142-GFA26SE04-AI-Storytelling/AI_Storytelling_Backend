using StoryPlatform.AI.Application.Abstractions.Prompting;
using StoryPlatform.AI.Domain.Enums;

namespace StoryPlatform.AI.Infrastructure.PromptCatalog;

public sealed class PromptTemplateProvider : IPromptTemplateProvider
{
    private static readonly IReadOnlyDictionary<PromptType, PromptTemplate> Templates =
        new Dictionary<PromptType, PromptTemplate>
        {
            [PromptType.Outline] = new(
                "outline-v1",
                "Create a child-safe story outline from the JSON context below. Follow the requested language, age band and constraints. Return only the required structured data. Context: {{context}}"),
            [PromptType.Story] = new(
                "story-v1",
                "Expand the approved outline into a complete educational story package. Respect every supplied constraint and return only the required structured data. Context: {{context}}"),
            [PromptType.Refinement] = new(
                "refinement-v1",
                "Refine the story package to resolve the listed evaluation issues without changing its educational intent. Return only the required structured data. Context: {{context}}")
        };

    public PromptTemplate GetActive(PromptType promptType, string language, string ageBand) =>
        Templates.TryGetValue(promptType, out var template)
            ? template
            : throw new KeyNotFoundException($"No active prompt template exists for {promptType}.");
}
