using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StoryPlatform.AI.Application.Moderation;
using StoryPlatform.AI.Domain.Generation;

namespace StoryPlatform.AI.Infrastructure.LLM.VertexAI;

/// <summary>Lazy Vertex-only access; no global ILlmClient/provider fallback.</summary>
public sealed class VertexGrayZoneLlmClient(IServiceProvider services, IOptions<VertexAIOptions> options) : IGrayZoneLlmClient
{
    public Task<LlmGenerationResult> EvaluateAsync(string prompt, JsonElement schema, CancellationToken ct)
    {
        var settings = options.Value;
        if (!string.Equals(settings.AuthMode, "Adc", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(settings.ProjectId)
            || string.IsNullOrWhiteSpace(settings.Location) || string.IsNullOrWhiteSpace(settings.Model))
            throw new GrayZoneAiException("GRAY_ZONE_VERTEX_NOT_CONFIGURED", 503);
        VertexAIGeminiClient client;
        try { client = services.GetRequiredService<VertexAIGeminiClient>(); }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException)
        { throw new GrayZoneAiException("GRAY_ZONE_VERTEX_NOT_CONFIGURED", 503); }
        return client.GenerateStructuredAsync(prompt, "gray_zone_evaluation", schema, ct);
    }
}
