namespace StoryPlatform.Application.Features.AIStoryInput.Guardrails;

public interface IInputGuardrail
{
    Task<InputGuardrailResult> CheckAsync(InputGuardrailRequest request, CancellationToken cancellationToken = default);
}

public sealed record InputGuardrailRequest(
    string Topic,
    IReadOnlyList<string> Characters,
    string? Setting,
    string Lesson,
    IReadOnlyList<string> BlockedTerms,
    IReadOnlyList<string> RestrictedTerms);

public enum InputGuardrailDecision
{
    Allow,
    Block,
    Inconclusive,
    Error
}

public sealed record InputGuardrailResult(
    InputGuardrailDecision Decision,
    string ReasonCode,
    string FallbackMessage,
    bool CanRetry,
    string CheckVersion);
