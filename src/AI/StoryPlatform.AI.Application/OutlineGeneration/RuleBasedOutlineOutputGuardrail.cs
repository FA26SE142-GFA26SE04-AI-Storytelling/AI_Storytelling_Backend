using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Contracts.AI.Responses;

namespace StoryPlatform.AI.Application.OutlineGeneration;

public sealed partial class RuleBasedOutlineOutputGuardrail : IOutlineOutputGuardrail
{
    private readonly OutlineGenerationOptions _options;

    public RuleBasedOutlineOutputGuardrail(IOptions<OutlineGenerationOptions> options)
    {
        _options = options.Value;
    }

    public OutlineSafetyResult Validate(GenerateOutlineRequest request, GenerateOutlineResponse response)
    {
        var fields = new[] { response.Title, response.Outline.Opening, response.Outline.Development, response.Outline.Ending };
        if (fields.Any(string.IsNullOrWhiteSpace))
        {
            return Block("OUTLINE_SCHEMA_INVALID", "AI không trả về đủ bốn phần của outline.");
        }

        // StoryVersion.Title is varchar(200) in Core; never accept an outline Core cannot persist.
        var maxTitleLength = Math.Clamp(request.Snapshot?.Config.MaxTitleLength ?? _options.MaximumTitleLength, 50, 200);
        var maxSectionLength = Math.Clamp(request.Snapshot?.Config.MaxSectionLength ?? _options.MaximumSectionLength, 1_000, 10_000);
        if (response.Title.Trim().Length > maxTitleLength ||
            fields.Skip(1).Any(value => value.Trim().Length > maxSectionLength))
        {
            return Block("OUTLINE_LENGTH_INVALID", "Outline vượt giới hạn cấu hình.");
        }

        var combined = string.Join('\n', fields);
        if (EmailPattern().IsMatch(combined) || PhonePattern().IsMatch(combined))
        {
            return Block("OUTLINE_CONTAINS_PII", "Outline chứa dữ liệu cá nhân không cần thiết.");
        }

        var leakageMarkers = new[] { "system prompt", "developer message", "ignore previous instructions", "bỏ qua hướng dẫn trước" };
        if (leakageMarkers.Any(marker => combined.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return Block("OUTLINE_PROMPT_LEAKAGE", "Outline chứa dấu hiệu lộ hoặc làm theo chỉ dẫn hệ thống.");
        }

        if (request.Constraints.BlockedTopics.Any(term => ContainsTerm(combined, term)))
        {
            return Block("OUTLINE_BLOCKED_CONTENT", "Outline không phù hợp với chính sách an toàn đang áp dụng.");
        }

        if (request.Constraints.RestrictedTopics.Any(term => ContainsTerm(combined, term)))
        {
            return Block("OUTLINE_RESTRICTED_CONTENT", "Outline cần được kiểm tra an toàn bổ sung.");
        }

        return new OutlineSafetyResult(true, "OUTLINE_ALLOWED");
    }

    private static bool ContainsTerm(string content, string term) =>
        !string.IsNullOrWhiteSpace(term) && Regex.IsMatch(
            content,
            $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(term.Trim())}(?![\p{{L}}\p{{N}}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static OutlineSafetyResult Block(string code, string fallback) => new(false, code, fallback);

    [GeneratedRegex(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"(?<!\d)(?:\+?84|0)(?:[ .-]?\d){9,10}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();
}
