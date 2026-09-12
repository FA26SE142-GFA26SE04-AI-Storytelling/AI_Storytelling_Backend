using System.Text.RegularExpressions;
using StoryPlatform.AI.Application.Abstractions.Evaluation;
using StoryPlatform.Contracts.AI.Models;

namespace StoryPlatform.AI.Infrastructure.Evaluation;

public sealed partial class RuleBasedStoryEvaluationService : IStoryEvaluationService
{
    private static readonly string[] RequiredQuizTypes = ["multiple_choice", "true_false", "short_answer"];

    public Task<EvaluationResultDto> EvaluateAsync(
        StoryPackageDto story,
        GenerationConstraintsDto constraints,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var issueDetails = new List<EvaluationIssueDto>();
        var searchableText = BuildSearchableText(story);
        var storyText = string.Join(' ', story.StorySections.Select(section => section.Content));
        var wordCount = WordRegex().Matches(storyText).Count;
        var sentenceCount = Math.Max(1, SentenceRegex().Matches(storyText).Count);
        var averageWordsPerSentence = wordCount == 0 ? 0 : Math.Round((double)wordCount / sentenceCount, 2);
        var maximumAverageWords = GetMaximumAverageWords(story.AgeBand);

        var schemaValid = !string.IsNullOrWhiteSpace(story.Title) &&
                          !string.IsNullOrWhiteSpace(story.AgeBand) &&
                          !string.IsNullOrWhiteSpace(story.ReadingLevel) &&
                          !string.IsNullOrWhiteSpace(story.VocabularyLevel) &&
                          !string.IsNullOrWhiteSpace(story.Lesson) &&
                          story.StorySections.Count > 0 &&
                          story.StorySections.All(section => section.Order > 0 && !string.IsNullOrWhiteSpace(section.Content)) &&
                          story.StorySections.Select(section => section.Order).Distinct().Count() == story.StorySections.Count;
        if (!schemaValid)
        {
            AddIssue(issueDetails, "schema_incomplete", "schema", "review_required", "$", "Story schema is incomplete.", true);
        }

        var blockedTopic = constraints.BlockedTopics
            .Where(topic => !string.IsNullOrWhiteSpace(topic))
            .FirstOrDefault(topic => searchableText.Contains(topic.Trim(), StringComparison.OrdinalIgnoreCase));
        var safetyPassed = blockedTopic is null;
        if (!safetyPassed)
        {
            AddIssue(issueDetails, "blocked_topic", "safety", "blocked", "$", $"Story contains blocked topic: {blockedTopic}.", false);
        }

        var lengthPassed = wordCount <= constraints.MaximumWords;
        if (!lengthPassed)
        {
            AddIssue(issueDetails, "maximum_words_exceeded", "readability", "review_required", "$.storySections", $"Story has {wordCount} words and exceeds maximumWords={constraints.MaximumWords}.", true);
        }

        var sentenceComplexityPassed = averageWordsPerSentence <= maximumAverageWords;
        if (!sentenceComplexityPassed)
        {
            AddIssue(issueDetails, "sentence_complexity_exceeded", "readability", "review_required", "$.storySections", $"Average sentence length is {averageWordsPerSentence} words; maximum for ageBand={story.AgeBand} is {maximumAverageWords}.", true);
        }

        var vocabularyPassed = story.Vocabulary.Count > 0 && story.Vocabulary.All(item =>
            !string.IsNullOrWhiteSpace(item.Word) &&
            !string.IsNullOrWhiteSpace(item.Meaning) &&
            !string.IsNullOrWhiteSpace(item.Example));
        if (!vocabularyPassed)
        {
            AddIssue(issueDetails, "vocabulary_invalid", "vocabulary", "review_required", "$.vocabulary", "Vocabulary must contain at least one complete word, meaning and example.", true);
        }

        var quizPassed = ValidateQuiz(story.Quiz, issueToken: issueDetails);
        var discussionPassed = story.DiscussionQuestions.Count > 0 &&
                               story.DiscussionQuestions.All(item => !string.IsNullOrWhiteSpace(item.Question));
        if (!discussionPassed)
        {
            AddIssue(issueDetails, "discussion_questions_invalid", "discussion", "review_required", "$.discussionQuestions", "At least one complete discussion question is required.", true);
        }

        var readabilityMetrics = new ReadabilityMetricsDto
        {
            Algorithm = IsVietnamese(story) ? "vi_average_sentence_length_v1" : "average_sentence_length_v1",
            WordCount = wordCount,
            SentenceCount = sentenceCount,
            AverageWordsPerSentence = averageWordsPerSentence
        };

        return Task.FromResult(new EvaluationResultDto
        {
            SchemaValid = schemaValid,
            SafetyPassed = safetyPassed,
            ReadabilityPassed = lengthPassed && sentenceComplexityPassed,
            VocabularyPassed = vocabularyPassed,
            QuizPassed = quizPassed,
            DiscussionPassed = discussionPassed,
            Issues = issueDetails.Select(issue => issue.Message).ToArray(),
            IssueDetails = issueDetails,
            ReadabilityMetrics = readabilityMetrics,
            SafetyScore = safetyPassed ? 1.0 : 0.0,
            FallbackMessage = safetyPassed
                ? null
                : "Nội dung này chưa phù hợp với thiết lập an toàn. Vui lòng đổi chủ đề, nhân vật hoặc bối cảnh rồi thử lại."
        });
    }

    private static string BuildSearchableText(StoryPackageDto story)
    {
        var values = new List<string>
        {
            story.Title,
            story.Lesson,
            story.Outline?.Opening ?? string.Empty,
            story.Outline?.Development ?? string.Empty,
            story.Outline?.Ending ?? string.Empty
        };

        values.AddRange(story.StorySections.SelectMany(section => new[] { section.Heading, section.Content }));
        values.AddRange(story.Vocabulary.SelectMany(item => new[] { item.Word, item.Meaning, item.Example }));
        values.AddRange(story.Quiz.SelectMany(item => new[] { item.Question, item.CorrectAnswer, item.Explanation }.Concat(item.Options)));
        values.AddRange(story.DiscussionQuestions.Select(item => item.Question));
        return string.Join(' ', values);
    }

    private static bool ValidateQuiz(IReadOnlyList<QuizItemDto> quiz, ICollection<EvaluationIssueDto> issueToken)
    {
        var valid = true;
        foreach (var requiredType in RequiredQuizTypes)
        {
            if (!quiz.Any(item => string.Equals(item.Type, requiredType, StringComparison.OrdinalIgnoreCase)))
            {
                AddIssue(issueToken, "quiz_type_missing", "quiz", "review_required", "$.quiz", $"Quiz type '{requiredType}' is required.", true);
                valid = false;
            }
        }

        for (var index = 0; index < quiz.Count; index++)
        {
            var item = quiz[index];
            var itemValid = !string.IsNullOrWhiteSpace(item.Question) && !string.IsNullOrWhiteSpace(item.Explanation);
            itemValid &= item.Type.ToLowerInvariant() switch
            {
                "multiple_choice" => item.Options.Count >= 2 && item.CorrectOptionIndex >= 0 && item.CorrectOptionIndex < item.Options.Count,
                "true_false" => string.Equals(item.CorrectAnswer, "true", StringComparison.OrdinalIgnoreCase) || string.Equals(item.CorrectAnswer, "false", StringComparison.OrdinalIgnoreCase),
                "short_answer" => !string.IsNullOrWhiteSpace(item.CorrectAnswer),
                _ => false
            };

            if (!itemValid)
            {
                AddIssue(issueToken, "quiz_item_invalid", "quiz", "review_required", $"$.quiz[{index}]", "Quiz item is inconsistent with its type.", true);
                valid = false;
            }
        }

        return valid;
    }

    private static void AddIssue(ICollection<EvaluationIssueDto> issues, string code, string stage, string severity, string path, string message, bool canRefine) =>
        issues.Add(new EvaluationIssueDto
        {
            Code = code,
            Stage = stage,
            Severity = severity,
            Path = path,
            Message = message,
            CanRefine = canRefine
        });

    private static double GetMaximumAverageWords(string ageBand) =>
        ageBand.Contains("6-8", StringComparison.OrdinalIgnoreCase) ? 14 : 22;

    private static bool IsVietnamese(StoryPackageDto story) =>
        story.Vocabulary.Any(item => item.Meaning.Any(character => character is 'ă' or 'â' or 'đ' or 'ê' or 'ô' or 'ơ' or 'ư'));

    [GeneratedRegex(@"\p{L}+(?:['’-]\p{L}+)?", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();

    [GeneratedRegex(@"[.!?]+", RegexOptions.CultureInvariant)]
    private static partial Regex SentenceRegex();
}
