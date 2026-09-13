using System.Text.RegularExpressions;

namespace StoryPlatform.Application.Features.Outline.Guardrails;

public sealed partial class RuleBasedOutlineReviewGuardrail : IOutlineReviewGuardrail
{
    public OutlineReviewSafetyResult Validate(
        string title,
        string opening,
        string development,
        string ending,
        IReadOnlyList<string> blockedTerms,
        IReadOnlyList<string> restrictedTerms)
    {
        var fields = new[] { title, opening, development, ending };
        if (fields.Any(string.IsNullOrWhiteSpace))
        {
            return Block("OUTLINE_SCHEMA_INVALID", "Title và ba phần outline đều bắt buộc.");
        }

        var combined = string.Join('\n', fields);
        if (EmailPattern().IsMatch(combined) || PhonePattern().IsMatch(combined))
        {
            return Block("OUTLINE_CONTAINS_PII", "Không đưa email hoặc số điện thoại vào outline.");
        }

        if (new[] { "system prompt", "developer message", "ignore previous instructions", "bỏ qua hướng dẫn trước" }
            .Any(marker => combined.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return Block("OUTLINE_PROMPT_LEAKAGE", "Outline chứa chỉ dẫn không phù hợp.");
        }

        if (blockedTerms.Any(term => ContainsTerm(combined, term)))
        {
            return Block("OUTLINE_BLOCKED_CONTENT", "Outline không phù hợp với Safety Policy.");
        }

        if (restrictedTerms.Any(term => ContainsTerm(combined, term)))
        {
            return Block("OUTLINE_RESTRICTED_CONTENT", "Outline cần được điều chỉnh trước khi lưu.");
        }

        return new OutlineReviewSafetyResult(true, "OUTLINE_ALLOWED");
    }

    private static bool ContainsTerm(string content, string term) =>
        !string.IsNullOrWhiteSpace(term) && Regex.IsMatch(
            content,
            $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(term.Trim())}(?![\p{{L}}\p{{N}}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static OutlineReviewSafetyResult Block(string code, string fallback) => new(false, code, fallback);

    [GeneratedRegex(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"(?<!\d)(?:\+?84|0)(?:[ .-]?\d){9,10}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();
}
