using StoryPlatform.Contracts.AI.Models;

namespace StoryPlatform.Contracts.AI.Requests;

public sealed record GenerateOutlineRequest
{
    public string RequestId { get; init; } = string.Empty;
    public string AgeBand { get; init; } = string.Empty;
    public string ReadingLevel { get; init; } = string.Empty;
    public string VocabularyLevel { get; init; } = string.Empty;
    public string Language { get; init; } = "vi";
    public string Source { get; init; } = "ai";
    public IReadOnlyList<string> Interests { get; init; } = [];
    public StoryParametersDto StoryParameters { get; init; } = new();
    public GenerationConstraintsDto Constraints { get; init; } = new();
}

public sealed record GenerateStoryRequest
{
    public string RequestId { get; init; } = string.Empty;
    public string AgeBand { get; init; } = string.Empty;
    public string ReadingLevel { get; init; } = string.Empty;
    public string VocabularyLevel { get; init; } = string.Empty;
    public string Language { get; init; } = "vi";
    public string Source { get; init; } = "ai";
    public string ApprovedOutlineReference { get; init; } = string.Empty;
    public StoryOutlineDto Outline { get; init; } = new(string.Empty, string.Empty, string.Empty);
    public StoryParametersDto StoryParameters { get; init; } = new();
    public GenerationConstraintsDto Constraints { get; init; } = new();
}

public sealed record RefineStoryRequest
{
    public string RequestId { get; init; } = string.Empty;
    public StoryPackageDto Story { get; init; } = new();
    public string Language { get; init; } = "vi";
    public string ReadingLevel { get; init; } = string.Empty;
    public string VocabularyLevel { get; init; } = string.Empty;
    public IReadOnlyList<string> Reasons { get; init; } = [];
    public GenerationConstraintsDto Constraints { get; init; } = new();
}

public sealed record EvaluateStoryRequest
{
    public string RequestId { get; init; } = string.Empty;
    public StoryPackageDto Story { get; init; } = new();
    public GenerationConstraintsDto Constraints { get; init; } = new();
}
