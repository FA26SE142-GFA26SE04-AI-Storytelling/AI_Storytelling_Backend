using System.Text.RegularExpressions;

namespace StoryPlatform.Application.Features.AIStoryInput.Guardrails;

public sealed partial class RuleBasedInputGuardrail : IInputGuardrail
{
    public const string Version = "core-rule-based-input-v1";

    private static readonly string[] PromptInjectionMarkers =
    [
        "ignore previous instructions",
        "ignore all instructions",
        "bỏ qua hướng dẫn trước",
        "bỏ qua mọi hướng dẫn",
        "system prompt",
        "developer message"
    ];

    public Task<InputGuardrailResult> CheckAsync(InputGuardrailRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fields = new[] { request.Topic, request.Setting ?? string.Empty, request.Lesson }
            .Concat(request.Characters)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        var combined = string.Join('\n', fields);

        if (PromptInjectionMarkers.Any(marker => combined.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return Task.FromResult(Block("PROMPT_INJECTION", "Yêu cầu chứa chỉ dẫn không phù hợp. Vui lòng mô tả ý tưởng truyện mà không yêu cầu thay đổi quy tắc hệ thống."));
        }

        if (EmailPattern().IsMatch(combined) || PhonePattern().IsMatch(combined))
        {
            return Task.FromResult(Block("UNNECESSARY_PERSONAL_DATA", "Không nhập email hoặc số điện thoại vào nội dung sáng tác."));
        }

        var blocked = FindPolicyMatch(fields, request.BlockedTerms);
        if (blocked is not null)
        {
            return Task.FromResult(Block("BLOCKED_CONTENT_CATEGORY", "Chủ đề này không phù hợp với chính sách an toàn đang áp dụng. Vui lòng chọn ý tưởng khác."));
        }

        var restricted = FindPolicyMatch(fields, request.RestrictedTerms);
        if (restricted is not null)
        {
            return Task.FromResult(new InputGuardrailResult(
                InputGuardrailDecision.Inconclusive,
                "RESTRICTED_CONTENT_REQUIRES_REVIEW",
                "Ý tưởng cần được điều chỉnh hoặc kiểm tra thêm trước khi có thể tạo truyện.",
                false,
                Version));
        }

        return Task.FromResult(new InputGuardrailResult(
            InputGuardrailDecision.Allow,
            "INPUT_ALLOWED",
            string.Empty,
            false,
            Version));
    }

    private static InputGuardrailResult Block(string reasonCode, string fallback) =>
        new(InputGuardrailDecision.Block, reasonCode, fallback, false, Version);

    private static string? FindPolicyMatch(IEnumerable<string> fields, IEnumerable<string> terms)
    {
        var normalizedTerms = terms
            .Select(Normalize)
            .Where(term => term.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return normalizedTerms.FirstOrDefault(term => fields.Any(field => ContainsPolicyTerm(field, term)));
    }

    private static string Normalize(string value) => string.Join(' ', value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static bool ContainsPolicyTerm(string value, string term)
    {
        var normalized = Normalize(value);
        var match = Regex.Match(
            normalized,
            $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(term)}(?![\p{{L}}\p{{N}}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return false;
        }

        var prefixStart = Math.Max(0, match.Index - 24);
        var localPrefix = normalized[prefixStart..match.Index].Trim().ToLowerInvariant();
        var educationalPrefixes = new[] { "chống", "phòng chống", "phòng tránh", "ngăn chặn", "nói không với" };
        return !educationalPrefixes.Any(prefix => localPrefix.EndsWith(prefix, StringComparison.Ordinal));
    }

    [GeneratedRegex(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"(?<!\d)(?:\+?84|0)(?:[ .-]?\d){9,10}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();
}
