using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.AI.Application.Evaluation;
using StoryPlatform.AI.Application.OutlineGeneration;
using StoryPlatform.AI.Application.Refinement;
using StoryPlatform.AI.Application.StoryGeneration;

namespace StoryPlatform.AI.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddAIApplication(this IServiceCollection services)
    {
        services.AddScoped<GenerateOutlineHandler>();
        services.AddScoped<GenerateStoryHandler>();
        services.AddScoped<RefineStoryHandler>();
        services.AddScoped<EvaluateStoryHandler>();
        return services;
    }
}
