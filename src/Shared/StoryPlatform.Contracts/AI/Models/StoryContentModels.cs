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
    public string Question { get; init; } = string.Empty;
    public IReadOnlyList<string> Options { get; init; } = [];
    public int CorrectOptionIndex { get; init; }
    public string Explanation { get; init; } = string.Empty;
}

public sealed record DiscussionQuestionDto(string Question);

public sealed record StoryPackageDto
{
    public string Title { get; init; } = string.Empty;
    public string AgeBand { get; init; } = string.Empty;
    public string ReadingLevel { get; init; } = string.Empty;
    public string VocabularyLevel { get; init; } = string.Empty;
    public IReadOnlyList<StorySectionDto> StorySections { get; init; } = [];
    public string Lesson { get; init; } = string.Empty;
    public IReadOnlyList<VocabularyItemDto> Vocabulary { get; init; } = [];
    public IReadOnlyList<QuizItemDto> Quiz { get; init; } = [];
    public IReadOnlyList<DiscussionQuestionDto> DiscussionQuestions { get; init; } = [];
}
