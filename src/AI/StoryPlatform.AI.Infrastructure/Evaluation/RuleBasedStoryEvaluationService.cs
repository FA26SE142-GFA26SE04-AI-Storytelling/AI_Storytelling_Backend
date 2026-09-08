using System.Text.RegularExpressions;
using StoryPlatform.AI.Application.Abstractions.Evaluation;
using StoryPlatform.Contracts.AI.Models;

namespace StoryPlatform.AI.Infrastructure.Evaluation;

public sealed partial class RuleBasedStoryEvaluationService : IStoryEvaluationService
{
    public Task<EvaluationResultDto> EvaluateAsync(
        StoryPackageDto story,
        GenerationConstraintsDto constraints,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var issues = new List<string>();
        var text = string.Join(' ', story.StorySections.Select(section => section.Content));
        var wordCount = WordRegex().Matches(text).Count;
        var schemaValid = !string.IsNullOrWhiteSpace(story.Title) && story.StorySections.Count > 0 &&
                          story.StorySections.All(section => !string.IsNullOrWhiteSpace(section.Content));

        if (!schemaValid)
        {
            issues.Add("Story schema is incomplete.");
        }

        var blockedTopic = constraints.BlockedTopics.FirstOrDefault(topic =>
            text.Contains(topic, StringComparison.OrdinalIgnoreCase) ||
            story.Title.Contains(topic, StringComparison.OrdinalIgnoreCase));
        var safetyPassed = blockedTopic is null;
        if (!safetyPassed)
        {
            issues.Add($"Story contains blocked topic: {blockedTopic}.");
        }

        var readabilityPassed = wordCount <= constraints.MaximumWords;
        if (!readabilityPassed)
        {
            issues.Add($"Story has {wordCount} words and exceeds maximumWords={constraints.MaximumWords}.");
        }

        var vocabularyPassed = story.Vocabulary.All(item =>
            !string.IsNullOrWhiteSpace(item.Word) && !string.IsNullOrWhiteSpace(item.Meaning));
        if (!vocabularyPassed)
        {
            issues.Add("Vocabulary entries must contain a word and a meaning.");
        }

        return Task.FromResult(new EvaluationResultDto
        {
            SchemaValid = schemaValid,
            SafetyPassed = safetyPassed,
            ReadabilityPassed = readabilityPassed,
            VocabularyPassed = vocabularyPassed,
            Issues = issues
        });
    }

    [GeneratedRegex(@"\p{L}+(?:['’-]\p{L}+)?", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();
}
