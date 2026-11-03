using System.Text.Json;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StoryPlatform.AI.Application.Abstractions.LLM;
using StoryPlatform.AI.Domain.Generation;

namespace StoryPlatform.AI.Application.ContentGeneration;

public sealed class StoryContentGenerationOptions
{
    public const string SectionName = "AI:StoryContentGeneration";
    public int MaxTechnicalAttempts { get; set; } = 3;
    public int TimeoutSeconds { get; set; } = 120;
    public int BaseRetryDelaySeconds { get; set; } = 2;
}

internal sealed record StoryContentGenerationResult<T>(T Value, LlmGenerationResult Generation, int AttemptCount);

public sealed class StoryContentGenerationExecutor
{
    private readonly ILlmClient _llmClient;
    private readonly StoryContentGenerationOptions _options;
    private readonly ILogger<StoryContentGenerationExecutor> _logger;

    public StoryContentGenerationExecutor(ILlmClient llmClient, IOptions<StoryContentGenerationOptions> options,
        ILogger<StoryContentGenerationExecutor>? logger = null)
    {
        _llmClient = llmClient;
        _options = options.Value;
        _logger = logger ?? NullLogger<StoryContentGenerationExecutor>.Instance;
    }

    internal async Task<StoryContentGenerationResult<T>> ExecuteAsync<T>(
        string prompt,
        string schemaName,
        JsonElement schema,
        Func<string, T?> deserialize,
        CancellationToken cancellationToken, int? technicalAttempts = null, int? executionBudgetSeconds = null)
        where T : class
    {
        if (executionBudgetSeconds is null)
            return await ExecuteWithinBudgetAsync(prompt, schemaName, schema, deserialize, cancellationToken, technicalAttempts);
        if (executionBudgetSeconds is < 1 or > 300)
            throw new ArgumentOutOfRangeException(nameof(executionBudgetSeconds));
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(executionBudgetSeconds.Value));
        try
        {
            return await ExecuteWithinBudgetAsync(prompt, schemaName, schema, deserialize, budget.Token, technicalAttempts);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && budget.IsCancellationRequested)
        {
            throw new TimeoutException("AI content request exhausted its execution budget.");
        }
    }

    private async Task<StoryContentGenerationResult<T>> ExecuteWithinBudgetAsync<T>(
        string prompt, string schemaName, JsonElement schema, Func<string, T?> deserialize,
        CancellationToken cancellationToken, int? technicalAttempts)
        where T : class
    {
        var maxAttempts = Math.Clamp(technicalAttempts ?? _options.MaxTechnicalAttempts, 1, 3);
        Exception? lastError = null;
        var elapsed = Stopwatch.StartNew();
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (attempt > 1)
            {
                var delay = GetRetryDelay(attempt, lastError);
                _logger.LogInformation("AI retry waiting. SchemaName={SchemaName} Attempt={Attempt} DelayMs={DelayMs}", schemaName, attempt, delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken);
            }

            try
            {
                _logger.LogInformation("AI generation attempt started. SchemaName={SchemaName} Attempt={Attempt} MaxAttempts={MaxAttempts}", schemaName, attempt, maxAttempts);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 10, 300)));
                var result = await _llmClient.GenerateStructuredAsync(prompt, schemaName, schema, timeout.Token);
                var value = deserialize(result.Content) ?? throw new JsonException("The LLM returned an empty payload.");
                _logger.LogInformation("AI output parsed and validated. SchemaName={SchemaName} Attempt={Attempt} ElapsedMs={ElapsedMs}", schemaName, attempt, elapsed.ElapsedMilliseconds);
                return new StoryContentGenerationResult<T>(value, result, attempt);
            }
            catch (Exception exception) when (IsRetryable(exception, cancellationToken))
            {
                _logger.LogWarning("AI attempt rejected. SchemaName={SchemaName} Attempt={Attempt} ErrorType={ErrorType} ElapsedMs={ElapsedMs}", schemaName, attempt, exception.GetType().Name, elapsed.ElapsedMilliseconds);
                // A durable Core batch owns the retry budget and needs the original status.
                // Leave a long backoff to the caller instead of retrying before the provider's deadline.
                if (technicalAttempts == 1 || ProviderRetryDelay(exception) > MaxProviderRetryDelay) throw;
                lastError = exception;
                if (attempt == maxAttempts)
                {
                    break;
                }
            }
        }

        throw new InvalidOperationException("Story content AI generation failed after the configured attempts.", lastError);
    }

    /// <summary>Maximum wait accepted from a provider hint; guards against a hostile or broken Retry-After.</summary>
    private static readonly TimeSpan MaxProviderRetryDelay = TimeSpan.FromSeconds(60);

    private TimeSpan GetRetryDelay(int attempt, Exception? lastError)
    {
        // A provider-supplied backoff always wins: retrying earlier lands in the same quota window and
        // simply burns another attempt.
        if (ProviderRetryDelay(lastError) is { } providerDelay)
        {
            return providerDelay;
        }

        var baseDelaySeconds = Math.Max(0, _options.BaseRetryDelaySeconds);
        if (baseDelaySeconds == 0)
        {
            return TimeSpan.Zero;
        }

        var exponentialDelay = baseDelaySeconds * Math.Pow(2, attempt - 2);
        var jitter = Random.Shared.NextDouble();
        return TimeSpan.FromSeconds(exponentialDelay + jitter);
    }

    private static TimeSpan? ProviderRetryDelay(Exception? exception)
    {
        if (exception is HttpRequestException { Data: { } data } &&
            data[VertexRetryAfterKeyForExecutor] is double seconds &&
            double.IsFinite(seconds) && seconds > 0 && seconds <= TimeSpan.MaxValue.TotalSeconds)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return null;
    }

    /// <summary>Mirrors <c>VertexAIGeminiClient.RetryAfterSecondsKey</c>; kept local to avoid a layering dependency.</summary>
    private const string VertexRetryAfterKeyForExecutor = "RetryAfterSeconds";

    private static bool IsRetryable(Exception exception, CancellationToken callerToken) => exception switch
    {
        // Truncation is deterministic under a fixed output ceiling; retrying only re-burns provider quota.
        StoryPlatform.AI.Domain.Generation.LlmOutputTruncatedException => false,
        JsonException => true,
        OperationCanceledException when !callerToken.IsCancellationRequested => true,
        HttpRequestException http => !http.StatusCode.HasValue ||
                                     (int)http.StatusCode.Value is 408 or 429 ||
                                     (int)http.StatusCode.Value >= 500,
        _ => false
    };
}
