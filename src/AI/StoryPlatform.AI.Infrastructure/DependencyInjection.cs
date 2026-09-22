using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.AI.Application.Abstractions.Evaluation;
using StoryPlatform.AI.Application.Abstractions.LLM;
using StoryPlatform.AI.Application.Abstractions.Prompting;
using StoryPlatform.AI.Application.StoryGeneration;
using StoryPlatform.AI.Application.OutlineGeneration;
using StoryPlatform.AI.Application.ContentGeneration;
using StoryPlatform.AI.Infrastructure.Evaluation;
using StoryPlatform.AI.Infrastructure.LLM.OpenAI;
using StoryPlatform.AI.Infrastructure.LLM.Gemini;
using StoryPlatform.AI.Infrastructure.LLM.VertexAI;
using StoryPlatform.AI.Infrastructure.PromptCatalog;

namespace StoryPlatform.AI.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAIInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OpenAIOptions>(configuration.GetSection(OpenAIOptions.SectionName));
        services.AddOptions<GeminiOptions>()
            .Bind(configuration.GetSection(GeminiOptions.SectionName))
            .PostConfigure(options =>
            {
                options.ApiKey = configuration["GEMINI_API_KEY"] ?? options.ApiKey;
            });
        services.AddOptions<VertexAIOptions>()
            .Bind(configuration.GetSection(VertexAIOptions.SectionName))
            .PostConfigure(options =>
            {
                options.ProjectId = configuration["AI__Google__ProjectId"] ?? options.ProjectId;
                options.Location = configuration["AI__Google__Location"] ?? options.Location;
                options.Model = configuration["AI__Google__Model"] ?? options.Model;
                options.AuthMode = configuration["AI__Google__AuthMode"] ?? options.AuthMode;
            });
        services.Configure<GenerateStoryOptions>(configuration.GetSection(GenerateStoryOptions.SectionName));
        services.Configure<OutlineGenerationOptions>(configuration.GetSection(OutlineGenerationOptions.SectionName));
        services.Configure<StoryContentGenerationOptions>(configuration.GetSection(StoryContentGenerationOptions.SectionName));
        var provider = configuration["AI:Provider"] ?? configuration["AI_PROVIDER"];
        var googleAuthMode = configuration["AI:Google:AuthMode"] ?? configuration["AI__Google__AuthMode"];
        if (string.Equals(provider, "Vertex", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(googleAuthMode, "Adc", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<ILlmClient, VertexAIGeminiClient>();
        }
        else if (string.Equals(provider, "Gemini", StringComparison.OrdinalIgnoreCase) ||
                 (string.IsNullOrWhiteSpace(provider) && !string.IsNullOrWhiteSpace(configuration["GEMINI_API_KEY"])))
        {
            services.AddHttpClient<ILlmClient, GeminiLlmClient>();
        }
        else
        {
            services.AddHttpClient<ILlmClient, OpenAILlmClient>();
        }
        services.AddSingleton<IPromptTemplateProvider, PromptTemplateProvider>();
        services.AddSingleton<IStoryEvaluationService, RuleBasedStoryEvaluationService>();
        return services;
    }
}
