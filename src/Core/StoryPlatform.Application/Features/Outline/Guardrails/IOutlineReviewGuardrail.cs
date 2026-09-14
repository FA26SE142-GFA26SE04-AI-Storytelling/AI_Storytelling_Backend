namespace StoryPlatform.Application.Features.Outline.Guardrails;

public sealed record OutlineReviewSafetyResult(bool IsAllowed, string ReasonCode, string? FallbackMessage = null);

public interface IOutlineReviewGuardrail
{
    OutlineReviewSafetyResult Validate(
        string title,
        string opening,
        string development,
        string ending,
        IReadOnlyList<string> blockedTerms,
        IReadOnlyList<string> restrictedTerms);
}
