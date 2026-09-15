using System.Text.Json;
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

    public StoryContentGenerationExecutor(ILlmClient llmClient, IOptions<StoryContentGenerationOptions> options)
    {
        _llmClient = llmClient;
        _options = options.Value;
    }

    internal async Task<StoryContentGenerationResult<T>> ExecuteAsync<T>(
        string prompt,
        string schemaName,
        JsonElement schema,
        Func<string, T?> deserialize,
        CancellationToken cancellationToken)
        where T : class
    {
        var maxAttempts = Math.Clamp(_options.MaxTechnicalAttempts, 1, 3);
        Exception? lastError = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (attempt > 1)
            {
                var seconds = Math.Max(0, _options.BaseRetryDelaySeconds) * Math.Pow(2, attempt - 2);
                await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
            }

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 10, 300)));
                var result = await _llmClient.GenerateStructuredAsync(prompt, schemaName, schema, timeout.Token);
                var value = deserialize(result.Content) ?? throw new JsonException("The LLM returned an empty payload.");
                return new StoryContentGenerationResult<T>(value, result, attempt);
            }
            catch (Exception exception) when (IsRetryable(exception, cancellationToken) && attempt < maxAttempts)
            {
                lastError = exception;
            }
        }

        throw new InvalidOperationException("Story content AI generation failed after the configured attempts.", lastError);
    }

    private static bool IsRetryable(Exception exception, CancellationToken callerToken) => exception switch
    {
        JsonException => true,
        OperationCanceledException when !callerToken.IsCancellationRequested => true,
        HttpRequestException http => !http.StatusCode.HasValue ||
                                     (int)http.StatusCode.Value is 408 or 429 ||
                                     (int)http.StatusCode.Value >= 500,
        _ => false
    };
}
