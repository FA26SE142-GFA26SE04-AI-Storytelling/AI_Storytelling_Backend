using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.AI.Application.Abstractions.Evaluation;
using StoryPlatform.AI.Application.Abstractions.LLM;
using StoryPlatform.AI.Application.Abstractions.Prompting;
using StoryPlatform.AI.Application.StoryGeneration;
using StoryPlatform.AI.Infrastructure.Evaluation;
using StoryPlatform.AI.Infrastructure.LLM.OpenAI;
using StoryPlatform.AI.Infrastructure.PromptCatalog;

namespace StoryPlatform.AI.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAIInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OpenAIOptions>(configuration.GetSection(OpenAIOptions.SectionName));
        services.Configure<GenerateStoryOptions>(configuration.GetSection(GenerateStoryOptions.SectionName));
        services.AddHttpClient<ILlmClient, OpenAILlmClient>();
        services.AddSingleton<IPromptTemplateProvider, PromptTemplateProvider>();
        services.AddSingleton<IStoryEvaluationService, RuleBasedStoryEvaluationService>();
        return services;
    }
}
