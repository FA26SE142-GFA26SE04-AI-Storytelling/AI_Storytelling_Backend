using System.Text.Json;
using Microsoft.Extensions.Options;
using StoryPlatform.AI.Application.Abstractions.LLM;
using StoryPlatform.AI.Application.Abstractions.Prompting;
using StoryPlatform.AI.Application.Common;
using StoryPlatform.AI.Domain.Enums;
using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Contracts.AI.Responses;

namespace StoryPlatform.AI.Application.OutlineGeneration;

public sealed class GenerateOutlineHandler
{
    private readonly ILlmClient _llmClient;
    private readonly IPromptTemplateProvider _promptProvider;
    private readonly IOutlineOutputGuardrail _outputGuardrail;
    private readonly OutlineGenerationOptions _options;

    public GenerateOutlineHandler(
        ILlmClient llmClient,
        IPromptTemplateProvider promptProvider,
        IOutlineOutputGuardrail outputGuardrail,
        IOptions<OutlineGenerationOptions> options)
    {
        _llmClient = llmClient;
        _promptProvider = promptProvider;
        _outputGuardrail = outputGuardrail;
        _options = options.Value;
    }

    public async Task<GenerateOutlineResponse> HandleAsync(GenerateOutlineRequest request, CancellationToken cancellationToken = default)
    {
        RequestGuard.Validate(request);
        var template = _promptProvider.GetActiveForOutline(request, Domain.Enums.PromptType.Outline);
        var prompt = template.Template;
        var maxAttempts = Math.Clamp(_options.MaxAttempts, 1, 3);
        Exception? lastError = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (attempt > 1)
            {
                var delay = TimeSpan.FromSeconds(Math.Max(0, _options.BaseRetryDelaySeconds) * Math.Pow(2, attempt - 2));
                await Task.Delay(delay, cancellationToken);
            }

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 10, 300)));
                var result = await _llmClient.GenerateStructuredAsync(
                    prompt,
                    "story_outline",
                    GenerationSchemas.Outline,
                    timeout.Token);
                var payload = JsonSerializer.Deserialize<OutlinePayload>(result.Content, JsonDefaults.Options)
                              ?? throw new JsonException("The LLM returned an empty outline payload.");
                var response = new GenerateOutlineResponse
                {
                    RequestId = request.RequestId,
                    GenerationId = Guid.NewGuid().ToString("N"),
                    Title = payload.Title.Trim(),
                    Outline = new StoryOutlineDto(
                        payload.Outline.Opening.Trim(),
                        payload.Outline.Development.Trim(),
                        payload.Outline.Ending.Trim()),
                    Metadata = result.ToMetadata(template.Version) with { AttemptCount = attempt }
                };
                var safety = _outputGuardrail.Validate(request, response);
                if (!safety.IsAllowed)
                {
                    throw new OutlineRejectedException(safety.ReasonCode, safety.FallbackMessage);
                }

                return response;
            }
            catch (OutlineRejectedException)
            {
                throw;
            }
            catch (Exception exception) when (IsRetryable(exception, cancellationToken) && attempt < maxAttempts)
            {
                lastError = exception;
            }
        }

        throw new InvalidOperationException("Outline generation failed after the configured attempts.", lastError);
    }

    private static bool IsRetryable(Exception exception, CancellationToken callerToken) => exception switch
    {
        JsonException => true,
        OperationCanceledException when !callerToken.IsCancellationRequested => true,
        HttpRequestException http => !http.StatusCode.HasValue ||
                                     (int)http.StatusCode.Value == 408 ||
                                     (int)http.StatusCode.Value == 429 ||
                                     (int)http.StatusCode.Value >= 500,
        _ => false
    };

    private sealed record OutlinePayload(string Title, StoryOutlineDto Outline);
}

public sealed class OutlineRejectedException : Exception
{
    public OutlineRejectedException(string reasonCode, string? fallbackMessage)
        : base(fallbackMessage ?? "Outline failed output safety validation.")
    {
        ReasonCode = reasonCode;
    }

    public string ReasonCode { get; }
}
