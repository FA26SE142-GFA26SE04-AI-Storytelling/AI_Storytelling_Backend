using System.Diagnostics;
using System.Net;
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
        : this(options, logger, null)
    {
    }

    internal VertexAIGeminiClient(
        IOptions<VertexAIOptions> options,
        ILogger<VertexAIGeminiClient> logger,
        Client? client)
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
        if (_options.TimeoutSeconds is < 1 or > 300)
            throw new InvalidOperationException("AI:Google:TimeoutSeconds must be between 1 and 300.");

        _client = client ?? new Client(
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

        var config = new GenerateContentConfig
        {
            ResponseMimeType = "application/json",
            ResponseSchema = BuildSchema(schema),
            HttpOptions = new HttpOptions { Timeout = checked(_options.TimeoutSeconds * 1000) }
        };
        // Content executor owns retry/backoff; SDK retries would consume its budget invisibly.
        if (schemaName is "story_content" or "refined_story_content" or "content_safety")
            config.HttpOptions.RetryOptions = new HttpRetryOptions { Attempts = 1 };
        if (IsSensitiveSchema(schemaName))
        {
            // SDK defaults to five attempts. Core owns the overall deadline; a retry is an explicit new operation.
            config.HttpOptions.RetryOptions = new HttpRetryOptions { Attempts = 1 };
            // Bound output including reasoning while allowing local prose and up to twenty review findings.
            config.MaxOutputTokens = schemaName == "creator_assistance_safety" ? 4096 : schemaName.StartsWith("gray_zone", StringComparison.Ordinal) ? 8192 : 16384;
        }

        var stopwatch = Stopwatch.StartNew();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        LlmGenerationResult result;
        try
        {
            var response = await _client.Models.GenerateContentAsync(
                model: _options.Model,
                contents: prompt,
                config: config,
                cancellationToken: deadline.Token);
            stopwatch.Stop();

            if (response?.Candidates is null || response.Candidates.Count == 0)
            {
                throw new InvalidOperationException("Vertex AI response did not contain any candidates.");
            }

            var candidate = response.Candidates[0];
            // A truncated candidate is a capacity symptom, not corrupt model output. Reading the usage numbers
            // below is what settles whether the output budget is actually the constraint.
            var finishReason = candidate.FinishReason?.Value;
            var truncated = string.Equals(finishReason, "MAX_TOKENS", StringComparison.OrdinalIgnoreCase);
            var content = ExtractOutputText(candidate) ?? (truncated ? string.Empty : throw new InvalidOperationException(
                "Vertex AI response did not contain structured output text."));
            var modelVersion = response.ModelVersion ?? _options.Model;
            var inputTokens = response.UsageMetadata?.PromptTokenCount ?? 0;
            var outputTokens = response.UsageMetadata?.CandidatesTokenCount ?? 0;
            // Thinking tokens bill as output but are absent from CandidatesTokenCount, so a quota complaint can
            // never be settled from the two headline numbers alone.
            var thoughtsTokens = response.UsageMetadata?.ThoughtsTokenCount ?? 0;
            var totalTokens = response.UsageMetadata?.TotalTokenCount ?? inputTokens + outputTokens + thoughtsTokens;

            if (truncated)
            {
                _logger.LogWarning(
                    "Vertex response hit the output token limit. Model={Model}, SchemaName={SchemaName}, " +
                    "FinishReason={FinishReason}, MaxOutputTokens={MaxOutputTokens}, InputTokens={InputTokens}, " +
                    "OutputTokens={OutputTokens}, ThoughtsTokens={ThoughtsTokens}, ContentLength={ContentLength}",
                    _options.Model, schemaName, finishReason, config.MaxOutputTokens,
                    inputTokens, outputTokens, thoughtsTokens, content.Length);
            }

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
                "InputTokens={InputTokens}, OutputTokens={OutputTokens}, ThoughtsTokens={ThoughtsTokens}, " +
                "TotalTokens={TotalTokens}, FinishReason={FinishReason}, Status=Success",
                _options.Model,
                schemaName,
                result.LatencyMs,
                result.InputTokens,
                result.OutputTokens,
                thoughtsTokens,
                totalTokens,
                finishReason);

            // Truncated JSON cannot be salvaged, and repeating the request under the same ceiling truncates
            // again. Fail fast, after the numbers are logged, so the attempt budget is not spent re-earning it.
            if (truncated) throw new LlmOutputTruncatedException(
                $"Vertex AI stopped at the output token limit (MaxOutputTokens={config.MaxOutputTokens}) for schema '{schemaName}'.");

        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw new TimeoutException("Vertex AI exceeded the configured request deadline.");
        }
        catch (LlmOutputTruncatedException)
        {
            // Preserve the capacity signal for middleware and Core's non-retryable classification.
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            var statusCode = GetStatusCode(ex);
            if (IsSensitiveSchema(schemaName))
            {
                var retryAfter = GetRetryAfter(ex);
                if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
                    _logger.LogInformation("Sensitive AI provider call cancelled by caller. TraceId={TraceId}, SchemaName={SchemaName}, LatencyMs={LatencyMs}", Activity.Current?.TraceId.ToString(), schemaName, stopwatch.ElapsedMilliseconds);
                else
                    _logger.LogWarning("Sensitive AI provider failed. TraceId={TraceId}, SchemaName={SchemaName}, LatencyMs={LatencyMs}, StatusCode={StatusCode}, ErrorType={ErrorType}, RetryAfterSeconds={RetryAfterSeconds}, QuotaMetric={QuotaMetric}, QuotaValue={QuotaValue}, Diagnostic={Diagnostic}", Activity.Current?.TraceId.ToString(), schemaName, stopwatch.ElapsedMilliseconds, statusCode, ex.GetType().Name, retryAfter?.TotalSeconds, GetQuotaHint(ex, "quotaMetric") ?? GetQuotaHint(ex, "quotaId"), GetQuotaHint(ex, "quotaValue"), ClassifyProviderDiagnostic(ex.Message));
                if (ex is OperationCanceledException or TimeoutException) throw;
                if (statusCode is 408 or 504) throw new TimeoutException("Sensitive AI provider timed out.");
                var failure = new HttpRequestException("Sensitive AI provider failed.", null, statusCode.HasValue ? (HttpStatusCode)statusCode.Value : null);
                // The retry owner (Core) must know how long the provider asked us to wait, otherwise it retries
                // straight back into the same quota window and burns its attempt budget.
                if (retryAfter is { } delay)
                    failure.Data[RetryAfterSecondsKey] = delay.TotalSeconds;
                throw failure;
            }
            if (statusCode is 408 or 429 || statusCode >= 500)
            {
                _logger.LogWarning(
                    "Vertex request encountered a transient provider error. " +
                    "Model={Model}, SchemaName={SchemaName}, LatencyMs={LatencyMs}, " +
                    "Status=RetryableError, StatusCode={StatusCode}, ErrorType={ErrorType}, " +
                    "QuotaMetric={QuotaMetric}, QuotaValue={QuotaValue}, RetryAfterSeconds={RetryAfterSeconds}",
                    _options.Model,
                    schemaName,
                    stopwatch.ElapsedMilliseconds,
                    statusCode,
                    ex.GetType().Name,
                    GetQuotaHint(ex, "quotaMetric") ?? GetQuotaHint(ex, "quotaId"),
                    GetQuotaHint(ex, "quotaValue"),
                    GetRetryAfter(ex)?.TotalSeconds);

                // Google.GenAI.ClientError exposes its own integer StatusCode while its
                // HttpRequestException.StatusCode can be null. Normalize the SDK error so
                // provider-agnostic retry policies can reliably recognize 408/429/5xx.
                var failure = new HttpRequestException(
                    "Vertex AI provider request failed.",
                    null,
                    (HttpStatusCode)statusCode.Value);
                if (GetRetryAfter(ex) is { } delay)
                    failure.Data[RetryAfterSecondsKey] = delay.TotalSeconds;
                throw failure;
            }

            // Structured error logging
            _logger.LogError(
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

    private static bool IsSensitiveSchema(string schemaName) => schemaName.StartsWith("creator_assistance", StringComparison.Ordinal)
        || schemaName.StartsWith("gray_zone", StringComparison.Ordinal)
        || schemaName.StartsWith("learning_", StringComparison.Ordinal)
        || schemaName == "wish_card_matching";

    private static int? GetStatusCode(Exception exception) => exception switch
    {
        ClientError clientError => clientError.StatusCode,
        ServerError serverError => serverError.StatusCode,
        HttpRequestException { StatusCode: not null } httpError => (int)httpError.StatusCode.Value,
        _ => null
    };

    // Return only fixed labels. Provider messages can echo prompts or credentials.
    internal static string ClassifyProviderDiagnostic(string message)
    {
        var labels = new List<string>();
        foreach (var field in new[] { "additionalProperties", "maxItems", "responseSchema", "maxOutputTokens", "thinkingBudget", "INVALID_ARGUMENT", "PERMISSION_DENIED", "NOT_FOUND" })
            if (message.Contains(field, StringComparison.OrdinalIgnoreCase)) labels.Add(field);
        return labels.Count == 0 ? "UNCLASSIFIED" : string.Join(",", labels);
    }

    /// <summary>
    /// Name of the <see cref="Exception.Data"/> entry that carries the provider-requested backoff
    /// (in seconds) across the AI service boundary.
    /// </summary>
    public const string RetryAfterSecondsKey = "RetryAfterSeconds";

    private static TimeSpan? GetRetryAfter(Exception exception)
    {
        // The Google.GenAI SDK surfaces the provider payload in the exception message. Only the retry
        // hint is read from it; nothing from the provider body is logged or returned to the caller.
        return ParseSeconds(exception.Message, "retryDelay");
    }

    /// <summary>
    /// Reads a named provider hint ("retryDelay", "quotaValue", ...) from the error payload the SDK embeds in the
    /// exception message. These fields are what identify a quota breach as a quota breach, so they must be
    /// observable without dumping the whole body.
    /// </summary>
    private static string? GetQuotaHint(Exception exception, string field)
    {
        // The value runs until a closing quote, comma or brace; it is only for logging, so a coarse match is fine.
        var pattern = "\"" + System.Text.RegularExpressions.Regex.Escape(field) + "\"\\s*:\\s*\"?(?<value>[^,\"}]+)";
        var match = System.Text.RegularExpressions.Regex.Match(exception.Message, pattern,
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        var value = match.Success ? match.Groups["value"].Value.Trim() : null;
        return string.IsNullOrWhiteSpace(value) || value.Length > 200 ? null : value;
    }

    private static TimeSpan? ParseSeconds(string? message, string field)
    {
        if (string.IsNullOrWhiteSpace(message) || message.IndexOf(field, StringComparison.OrdinalIgnoreCase) < 0)
        {
            return null;
        }

        var match = System.Text.RegularExpressions.Regex.Match(
            message,
            "\"" + System.Text.RegularExpressions.Regex.Escape(field) + @"""\s*:\s*""(?<value>[0-9.]+)s""",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return match.Success && double.TryParse(match.Groups["value"].Value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : null;
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
