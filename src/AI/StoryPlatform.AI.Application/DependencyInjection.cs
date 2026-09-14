using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.AI.Application.Evaluation;
using StoryPlatform.AI.Application.OutlineGeneration;
using StoryPlatform.AI.Application.Refinement;
using StoryPlatform.AI.Application.StoryGeneration;
using StoryPlatform.AI.Application.ContentGeneration;

namespace StoryPlatform.AI.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddAIApplication(this IServiceCollection services)
    {
        services.AddScoped<GenerateOutlineHandler>();
        services.AddSingleton<IOutlineOutputGuardrail, RuleBasedOutlineOutputGuardrail>();
        services.AddScoped<GenerateStoryHandler>();
        services.AddScoped<RefineStoryHandler>();
        services.AddScoped<EvaluateStoryHandler>();
        services.AddScoped<StoryContentGenerationExecutor>();
        services.AddScoped<GenerateStoryContentHandler>();
        services.AddScoped<RefineStoryContentHandler>();
        services.AddScoped<GenerateVocabularyHandler>();
        services.AddScoped<GenerateQuizHandler>();
        services.AddScoped<GenerateDiscussionHandler>();
        services.AddScoped<EvaluateContentSafetyHandler>();
        return services;
    }
}
