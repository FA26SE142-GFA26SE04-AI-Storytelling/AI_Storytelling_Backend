namespace StoryPlatform.Contracts.AI.Models;

public sealed record StoryOutlineDto(
    string Opening,
    string Development,
    string Ending);

public sealed record StorySectionDto(
    int Order,
    string Heading,
    string Content);

public sealed record VocabularyItemDto(
    string Word,
    string Meaning,
    string Example);

public sealed record QuizItemDto
{
    public string Type { get; init; } = string.Empty;
    public string Question { get; init; } = string.Empty;
    public IReadOnlyList<string> Options { get; init; } = [];
    public int CorrectOptionIndex { get; init; }
    public string CorrectAnswer { get; init; } = string.Empty;
    public string Explanation { get; init; } = string.Empty;
    public string LearningGoal { get; init; } = string.Empty;
    public string SourceQuote { get; init; } = string.Empty;
    public string Hint { get; init; } = string.Empty;
    public IReadOnlyList<string> OptionFeedback { get; init; } = [];
}

public sealed record DiscussionQuestionDto(string Question);

public sealed record StoryContentDto
{
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public IReadOnlyList<StorySectionDto> StorySections { get; init; } = [];
    public string Lesson { get; init; } = string.Empty;
}

public sealed record GeneratedVocabularyItemDto(string Term, string Definition)
{
    public string SourceQuote { get; init; } = string.Empty;
    public string Example { get; init; } = string.Empty;
}

public sealed record StoryPackageDto
{
    public string Source { get; init; } = "ai";
    public string Title { get; init; } = string.Empty;
    public string AgeBand { get; init; } = string.Empty;
    public string ReadingLevel { get; init; } = string.Empty;
    public string VocabularyLevel { get; init; } = string.Empty;
    public StoryOutlineDto? Outline { get; init; }
    public IReadOnlyList<StorySectionDto> StorySections { get; init; } = [];
    public string Lesson { get; init; } = string.Empty;
    public IReadOnlyList<VocabularyItemDto> Vocabulary { get; init; } = [];
    public IReadOnlyList<QuizItemDto> Quiz { get; init; } = [];
    public IReadOnlyList<DiscussionQuestionDto> DiscussionQuestions { get; init; } = [];
    public IReadOnlyList<string> SafetyTags { get; init; } = [];
    public ReadabilityMetricsDto? ReadabilityMetrics { get; init; }
    public string GenerationVersion { get; init; } = string.Empty;
}

public sealed record ReadabilityMetricsDto
{
    public string Algorithm { get; init; } = string.Empty;
    public int WordCount { get; init; }
    public int SentenceCount { get; init; }
    public double AverageWordsPerSentence { get; init; }
    public double? FleschKincaidGradeLevel { get; init; }
    public double? FleschReadingEase { get; init; }
}

public sealed record ListeningStoryInfoDto
{
    public string Title { get; init; } = string.Empty;
    public string Lesson { get; init; } = string.Empty;
}

public sealed record ListeningBlockDto(string BlockId, string Text);

/// <summary>Only the error codes of the previous attempt are sent back, never the generated text.</summary>
public sealed record ListeningPreviousErrorDto(string Code, int? ItemIndex, string? Field);

public sealed record ListeningChoiceDto
{
    public string? Text { get; init; }
    public string? Feedback { get; init; }
    public bool IsMatch { get; init; }
}

public sealed record CheckpointItemDto
{
    public string? Kind { get; init; }
    public string? AfterBlockId { get; init; }
    public string? Question { get; init; }
    public string? SourceQuote { get; init; }
    public string? Explanation { get; init; }
    public IReadOnlyList<ListeningChoiceDto> Options { get; init; } = [];
    public string? OtherFeedback { get; init; }
}

public sealed record RolePlayItemDto
{
    public string? AfterBlockId { get; init; }
    public string? CharacterName { get; init; }
    public string? LineQuote { get; init; }
    public string? PraiseFeedback { get; init; }
}

public sealed record FillBlankItemDto
{
    public string? AfterBlockId { get; init; }
    public string? SentenceQuote { get; init; }
    public string? HiddenWord { get; init; }
    public IReadOnlyList<string> AcceptedAnswers { get; init; } = [];
    public string? Hint { get; init; }
    public string? MatchFeedback { get; init; }
    public string? SuggestFeedback { get; init; }
}

public sealed record ListeningEventDto
{
    public int Order { get; init; }
    public string? BlockId { get; init; }
    public string? Text { get; init; }
    public string? SourceQuote { get; init; }
}

/// <summary>No afterBlockId: the backend always places event ordering after the last block.</summary>
public sealed record EventOrderingItemDto
{
    public IReadOnlyList<ListeningEventDto> Events { get; init; } = [];
    public string? MatchFeedback { get; init; }
    public string? RetryFeedback { get; init; }
}

public sealed record PicturePredictionOptionDto
{
    public string? Label { get; init; }
    public string? ImageDescription { get; init; }
    public string? Feedback { get; init; }
    public bool IsMatch { get; init; }
}

public sealed record PicturePredictionItemDto
{
    public string? AfterBlockId { get; init; }
    public string? RevealBlockId { get; init; }
    public string? Question { get; init; }
    public string? SourceQuote { get; init; }
    public string? WaitFeedback { get; init; }
    public IReadOnlyList<PicturePredictionOptionDto> Options { get; init; } = [];
}
