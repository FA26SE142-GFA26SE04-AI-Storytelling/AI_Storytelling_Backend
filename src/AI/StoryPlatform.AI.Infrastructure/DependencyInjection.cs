using StoryPlatform.AI.Application.Moderation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.AI.Application.Abstractions.Evaluation;
using StoryPlatform.AI.Application.Abstractions.LLM;
using StoryPlatform.AI.Application.Abstractions.Prompting;
using StoryPlatform.AI.Application.StoryGeneration;
using StoryPlatform.AI.Application.OutlineGeneration;
using StoryPlatform.AI.Application.ContentGeneration;
using StoryPlatform.AI.Infrastructure.Evaluation;
using StoryPlatform.AI.Infrastructure.LLM.Gemini;
using StoryPlatform.AI.Infrastructure.LLM.VertexAI;
using StoryPlatform.AI.Infrastructure.PromptCatalog;

namespace StoryPlatform.AI.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAIInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
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
        services.Configure<GrayZoneAiOptions>(configuration.GetSection(GrayZoneAiOptions.SectionName));
        services.AddSingleton<VertexAIGeminiClient>();
        services.AddSingleton<IGrayZoneLlmClient, VertexGrayZoneLlmClient>();
        services.Configure<GenerateStoryOptions>(configuration.GetSection(GenerateStoryOptions.SectionName));
        services.Configure<OutlineGenerationOptions>(configuration.GetSection(OutlineGenerationOptions.SectionName));
        services.Configure<StoryContentGenerationOptions>(configuration.GetSection(StoryContentGenerationOptions.SectionName));
        var provider = new[] { configuration["AI:Provider"], configuration["AI_PROVIDER"] }
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
        var googleAuthMode = configuration["AI:Google:AuthMode"] ?? configuration["AI__Google__AuthMode"];
        if (string.Equals(provider, "Vertex", StringComparison.OrdinalIgnoreCase) ||
            (string.IsNullOrWhiteSpace(provider) && string.Equals(googleAuthMode, "Adc", StringComparison.OrdinalIgnoreCase)))
        {
            services.AddSingleton<ILlmClient>(sp => sp.GetRequiredService<VertexAIGeminiClient>());
        }
        else if (string.Equals(provider, "Gemini", StringComparison.OrdinalIgnoreCase) ||
                 (string.IsNullOrWhiteSpace(provider) && !string.IsNullOrWhiteSpace(configuration["GEMINI_API_KEY"])))
        {
            services.AddHttpClient<ILlmClient, GeminiLlmClient>();
        }
        else
        {
            // Only Vertex AI and Gemini are supported providers. Falling back to a third-party
            // provider here would silently route story content to an unvetted service, so an
            // unsupported or missing configuration must stop the host instead.
            throw new InvalidOperationException(
                $"AI:Provider '{provider ?? "(unset)"}' is not supported. Use 'Vertex' or 'Gemini'.");
        }
        services.AddSingleton<IPromptTemplateProvider, PromptTemplateProvider>();
        services.AddSingleton<IStoryEvaluationService, RuleBasedStoryEvaluationService>();
        return services;
    }
}
