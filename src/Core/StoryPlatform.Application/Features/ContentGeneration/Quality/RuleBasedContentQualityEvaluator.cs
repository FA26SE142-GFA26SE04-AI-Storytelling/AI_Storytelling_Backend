using System.Text.RegularExpressions;
using StoryPlatform.Application.Features.AIStoryInput.Models;
using StoryPlatform.Contracts.AI.Models;

namespace StoryPlatform.Application.Features.ContentGeneration.Quality;

public sealed partial class RuleBasedContentQualityEvaluator : IContentQualityEvaluator
{
    public ContentQualityResult Evaluate(
        StoryContentDto story,
        StoryOutlineDto approvedOutline,
        AcceptedAIStoryInputSnapshot input,
        AIStoryInputContextSnapshot context)
    {
        var content = string.Join(' ', story.StorySections.OrderBy(item => item.Order).Select(item => item.Content));
        var searchable = string.Join(' ', story.Title, content, story.Lesson);
        var outlineTerms = SignificantTerms(string.Join(' ', approvedOutline.Opening, approvedOutline.Development, approvedOutline.Ending));
        var matchedOutlineTerms = outlineTerms.Count == 0 || outlineTerms.Count(term => ContainsTerm(searchable, term)) >= Math.Max(1, outlineTerms.Count / 5);
        var lessonTerms = SignificantTerms(input.Lesson);
        var lessonMatched = string.IsNullOrWhiteSpace(input.Lesson) || lessonTerms.Count == 0 ||
                            lessonTerms.Any(term => ContainsTerm(searchable, term));
        var outline = Gate(matchedOutlineTerms && lessonMatched, true, "CONTENT_OUTLINE_INCONSISTENT",
            "Nội dung chưa bám sát outline hoặc lesson goal đã duyệt.");

        var wordCount = WordPattern().Matches(content).Count;
        var minimum = Math.Max(100, (int)Math.Floor(input.TargetLength * 0.7));
        var length = Gate(wordCount >= minimum && wordCount <= context.MaximumLength, true, "CONTENT_LENGTH_INVALID",
            $"Độ dài {wordCount} từ phải nằm trong khoảng {minimum}-{context.MaximumLength} từ.");

        var blocked = PolicyTerms(context.BlockedCategoryTerms, context.BlockedCategoryCodes)
            .FirstOrDefault(term => ContainsTerm(searchable, term));
        var restricted = PolicyTerms(context.RestrictedCategoryTerms, context.RestrictedCategoryCodes)
            .FirstOrDefault(term => ContainsTerm(searchable, term));
        var containsPii = EmailPattern().IsMatch(searchable) || PhonePattern().IsMatch(searchable);
        var safetyPassed = blocked is null && restricted is null && !containsPii;
        var safetyReason = blocked is not null ? $"Nội dung chứa chủ đề bị chặn: {blocked}." :
            restricted is not null ? $"Nội dung chứa chủ đề hạn chế: {restricted}." : "Nội dung chứa dữ liệu cá nhân.";
        var safety = Gate(safetyPassed, false, "CONTENT_SAFETY_BLOCKED", safetyReason);

        var readabilityResult = ReadabilityCalculator.EvaluateForProfile(
            content,
            context.Language,
            context.ReadingLevel,
            context.ReadabilityScoreThreshold);
        var readability = Gate(readabilityResult.Passed, true, "CONTENT_READABILITY_NOT_MET",
            $"Readability {readabilityResult.Metrics.Algorithm} chưa phù hợp Reading Level {context.ReadingLevel}: " +
            $"grade={readabilityResult.Metrics.Fkgl:F2}/{readabilityResult.MaximumGradeLevel:F2}, " +
            $"ease={readabilityResult.Metrics.Fre:F2}/{readabilityResult.MinimumEaseScore:F2}, " +
            $"trung bình={readabilityResult.Metrics.AverageWordsPerSentence:F2}/{readabilityResult.MaximumAverageWordsPerSentence:F2} từ/câu.");

        var averageWordLength = wordCount == 0 ? 0 : WordPattern().Matches(content).Select(match => match.Value.Length).Average();
        var vocabularyLimit = context.VocabularyLevel.Contains("1", StringComparison.OrdinalIgnoreCase) ? 6.5 :
            context.VocabularyLevel.Contains("2", StringComparison.OrdinalIgnoreCase) ? 8.0 : 10.0;
        var vocabulary = Gate(averageWordLength <= vocabularyLimit, true, "CONTENT_VOCABULARY_LEVEL_NOT_MET",
            $"Độ dài từ trung bình {averageWordLength:F1} chưa phù hợp {context.VocabularyLevel}.");

        var passed = outline.Passed && length.Passed && safety.Passed && readability.Passed && vocabulary.Passed;
        return new ContentQualityResult(passed, outline, length, safety, readability, vocabulary, safetyPassed ? 1m : 0m);
    }

    private static ContentQualityGate Gate(bool passed, bool canRefine, string code, string violation) =>
        new(passed, passed || canRefine, passed ? null : code, passed ? [] : [violation]);

    private static IReadOnlyList<string> PolicyTerms(IReadOnlyList<string>? terms, IReadOnlyList<string> fallback) =>
        terms is { Count: > 0 } ? terms : fallback;

    private static IReadOnlyList<string> SignificantTerms(string value) => WordPattern().Matches(value)
        .Select(item => item.Value.Trim())
        .Where(item => item.Length >= 4)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static bool ContainsTerm(string content, string term) => !string.IsNullOrWhiteSpace(term) && Regex.IsMatch(
        content, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(term.Trim())}(?![\p{{L}}\p{{N}}])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [GeneratedRegex(@"\p{L}+(?:['’-]\p{L}+)?", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();
    [GeneratedRegex(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();
    [GeneratedRegex(@"(?<!\d)(?:\+?84|0)(?:[ .-]?\d){9,10}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();
}
