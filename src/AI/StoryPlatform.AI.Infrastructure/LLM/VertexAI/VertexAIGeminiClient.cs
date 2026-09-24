using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<VertexAIGeminiClient> _logger;

    public VertexAIGeminiClient(
        IOptions<VertexAIOptions> options,
        ILogger<VertexAIGeminiClient> logger)
    {
        _options = options.Value;
        _logger = logger;

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

        _logger.LogInformation(
            "VertexAIGeminiClient initialized. Project={ProjectId}, Location={Location}, Model={Model}",
            _options.ProjectId,
            _options.Location,
            _options.Model);
    }

    public async Task<LlmGenerationResult> GenerateStructuredAsync(
        string prompt,
        string schemaName,
        JsonElement schema,
        CancellationToken cancellationToken = default)
    {
        // Structured request logging
        _logger.LogInformation(
            "Vertex request started. " +
            "Model={Model}, Location={Location}, SchemaName={SchemaName}, PromptLength={PromptLength}",
            _options.Model,
            _options.Location,
            schemaName,
            prompt.Length);

        // Log full prompt in Debug mode (development only - contains user data)
        _logger.LogDebug(
            "Vertex request prompt. SchemaName={SchemaName}, Prompt={Prompt}",
            schemaName,
            prompt);

        var config = new GenerateContentConfig
        {
            ResponseMimeType = "application/json",
            ResponseSchema = BuildSchema(schema)
        };

        var stopwatch = Stopwatch.StartNew();

        LlmGenerationResult result;
        try
        {
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

            result = new LlmGenerationResult(
                content,
                "VertexAI",
                _options.Model,
                modelVersion,
                inputTokens,
                outputTokens,
                stopwatch.ElapsedMilliseconds);

            // Structured success logging
            _logger.LogInformation(
                "Vertex request completed. " +
                "Model={Model}, SchemaName={SchemaName}, LatencyMs={LatencyMs}, " +
                "InputTokens={InputTokens}, OutputTokens={OutputTokens}, Status=Success",
                _options.Model,
                schemaName,
                result.LatencyMs,
                result.InputTokens,
                result.OutputTokens);

            // Log full response in Debug mode
            _logger.LogDebug(
                "Vertex response content. SchemaName={SchemaName}, ContentLength={ContentLength}, Content={Content}",
                schemaName,
                content.Length,
                content);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            // Structured error logging
            _logger.LogError(
                ex,
                "Vertex request failed. " +
                "Model={Model}, SchemaName={SchemaName}, LatencyMs={LatencyMs}, Status=Error, ErrorType={ErrorType}",
                _options.Model,
                schemaName,
                stopwatch.ElapsedMilliseconds,
                ex.GetType().Name);

            throw;
        }

        return result;
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
