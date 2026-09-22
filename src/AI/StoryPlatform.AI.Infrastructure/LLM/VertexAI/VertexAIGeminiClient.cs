using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using StoryPlatform.AI.Application.Abstractions.LLM;
using StoryPlatform.AI.Domain.Generation;
using Google.GenAI;
using Google.GenAI.Types;

namespace StoryPlatform.AI.Infrastructure.LLM.VertexAI;

/// <summary>
/// Vertex AI implementation of <see cref="ILlmClient"/> using the official
/// <c>Google.GenAI</c> SDK and Application Default Credentials (ADC).
/// Designed for the AI story pipeline; Phase 5 media pipeline in src/Core
/// is intentionally untouched.
/// </summary>
public sealed class VertexAIGeminiClient : ILlmClient
{
    private readonly VertexAIOptions _options;
    private readonly Client _client;

    public VertexAIGeminiClient(IOptions<VertexAIOptions> options)
    {
        _options = options.Value;

        if (!string.Equals(_options.AuthMode, "Adc", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Unsupported AI:Google:AuthMode '{_options.AuthMode}'. Only 'Adc' is supported.");
        }

        if (string.IsNullOrWhiteSpace(_options.ProjectId))
        {
            throw new InvalidOperationException("AI:Google:ProjectId is not configured.");
        }

        if (string.IsNullOrWhiteSpace(_options.Location))
        {
            throw new InvalidOperationException("AI:Google:Location is not configured.");
        }

        if (string.IsNullOrWhiteSpace(_options.Model))
        {
            throw new InvalidOperationException("AI:Google:Model is not configured.");
        }

        _client = new Client(
            project: _options.ProjectId,
            location: _options.Location,
            vertexAI: true);
    }

    public async Task<LlmGenerationResult> GenerateStructuredAsync(
        string prompt,
        string schemaName,
        JsonElement schema,
        CancellationToken cancellationToken = default)
    {
        var config = new GenerateContentConfig
        {
            ResponseMimeType = "application/json",
            ResponseSchema = BuildSchema(schema)
        };

        var stopwatch = Stopwatch.StartNew();
        var response = await _client.Models.GenerateContentAsync(
            model: _options.Model,
            contents: prompt,
            config: config,
            cancellationToken: cancellationToken);
        stopwatch.Stop();

        if (response?.Candidates is null || response.Candidates.Count == 0)
        {
            throw new InvalidOperationException("Vertex AI response did not contain any candidates.");
        }

        var candidate = response.Candidates[0];
        var content = ExtractOutputText(candidate)
                      ?? throw new InvalidOperationException(
                          "Vertex AI response did not contain structured output text.");
        var modelVersion = response.ModelVersion ?? _options.Model;
        var inputTokens = response.UsageMetadata?.PromptTokenCount ?? 0;
        var outputTokens = response.UsageMetadata?.CandidatesTokenCount ?? 0;

        return new LlmGenerationResult(
            content,
            "VertexAI",
            _options.Model,
            modelVersion,
            inputTokens,
            outputTokens,
            stopwatch.ElapsedMilliseconds);
    }

    private static Schema BuildSchema(JsonElement schema)
    {
        // The SDK supports passing the raw JSON schema via Schema.Schema (object/dict);
        // for typed construction we fall back to serialising the JsonElement and
        // deserialising it into the strongly-typed Schema record.
        var rawJson = schema.GetRawText();
        var deserialized = JsonSerializer.Deserialize<Google.GenAI.Types.Schema>(
            rawJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        return deserialized ?? throw new InvalidOperationException(
            $"Failed to convert JSON schema '{schema}' to Google.GenAI.Types.Schema.");
    }

    private static string? ExtractOutputText(Candidate candidate)
    {
        if (candidate?.Content?.Parts is null || candidate.Content.Parts.Count == 0)
        {
            return null;
        }

        foreach (var part in candidate.Content.Parts)
        {
            if (!string.IsNullOrWhiteSpace(part?.Text))
            {
                return part.Text;
            }
        }

        return null;
    }
}
