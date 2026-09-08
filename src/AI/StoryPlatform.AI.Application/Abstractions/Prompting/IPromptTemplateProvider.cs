using StoryPlatform.AI.Domain.Enums;

namespace StoryPlatform.AI.Application.Abstractions.Prompting;

public sealed record PromptTemplate(string Version, string Template);

public interface IPromptTemplateProvider
{
    PromptTemplate GetActive(PromptType promptType, string language, string ageBand);
}
