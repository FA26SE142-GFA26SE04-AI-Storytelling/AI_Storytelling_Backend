using StoryPlatform.Contracts.AI.Models;

namespace StoryPlatform.Contracts.AI.Requests;

public sealed record GenerateOutlineRequest
{
    public AiGenerationSnapshot? Snapshot { get; init; }
    public string RequestId { get; init; } = string.Empty;
    public string AgeBand { get; init; } = string.Empty;
    public string ReadingLevel { get; init; } = string.Empty;
    public string VocabularyLevel { get; init; } = string.Empty;
    public string Language { get; init; } = "vi";
    public string Source { get; init; } = "ai";
    public IReadOnlyList<string> Interests { get; init; } = [];
    public string? ComprehensionGoal { get; init; }
    public StoryParametersDto StoryParameters { get; init; } = new();
    public GenerationConstraintsDto Constraints { get; init; } = new();
}

public sealed record GenerateStoryRequest
{
    public AiGenerationSnapshot? Snapshot { get; init; }
    public string RequestId { get; init; } = string.Empty;
    public string AgeBand { get; init; } = string.Empty;
    public string ReadingLevel { get; init; } = string.Empty;
    public string VocabularyLevel { get; init; } = string.Empty;
    public string Language { get; init; } = "vi";
    public string Source { get; init; } = "ai";
    public string ApprovedOutlineReference { get; init; } = string.Empty;
    public string? ComprehensionGoal { get; init; }
    public StoryOutlineDto Outline { get; init; } = new(string.Empty, string.Empty, string.Empty);
    public StoryParametersDto StoryParameters { get; init; } = new();
    public GenerationConstraintsDto Constraints { get; init; } = new();
}

public sealed record RefineStoryRequest
{
    public AiGenerationSnapshot? Snapshot { get; init; }
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
    public AiGenerationSnapshot? Snapshot { get; init; }
    public string RequestId { get; init; } = string.Empty;
    public StoryPackageDto Story { get; init; } = new();
    public GenerationConstraintsDto Constraints { get; init; } = new();
}

public sealed record GenerateStoryContentRequest
{
    public AiGenerationSnapshot? Snapshot { get; init; }
    public string RequestId { get; init; } = string.Empty;
    public string AgeBand { get; init; } = string.Empty;
    public string ReadingLevel { get; init; } = string.Empty;
    public string VocabularyLevel { get; init; } = string.Empty;
    public string Language { get; init; } = "vi";
    public string ApprovedOutlineReference { get; init; } = string.Empty;
    public string? ComprehensionGoal { get; init; }
    public StoryOutlineDto Outline { get; init; } = new(string.Empty, string.Empty, string.Empty);
    public StoryParametersDto StoryParameters { get; init; } = new();
    public GenerationConstraintsDto Constraints { get; init; } = new();
}

public sealed record RefineStoryContentRequest
{
    public AiGenerationSnapshot? Snapshot { get; init; }
    public string RequestId { get; init; } = string.Empty;
    public StoryContentDto Story { get; init; } = new();
    public StoryOutlineDto Outline { get; init; } = new(string.Empty, string.Empty, string.Empty);
    public string AgeBand { get; init; } = string.Empty;
    public string ReadingLevel { get; init; } = string.Empty;
    public string VocabularyLevel { get; init; } = string.Empty;
    public string Language { get; init; } = "vi";
    public int TargetLength { get; init; }
    public GenerationConstraintsDto Constraints { get; init; } = new();
    public IReadOnlyList<string> Reasons { get; init; } = [];
}

public sealed record GenerateVocabularyRequest
{
    public AiGenerationSnapshot? Snapshot { get; init; }
    public string RequestId { get; init; } = string.Empty;
    public StoryContentDto Story { get; init; } = new();
    public string AgeBand { get; init; } = string.Empty;
    public string ReadingLevel { get; init; } = string.Empty;
    public string VocabularyLevel { get; init; } = string.Empty;
    public string Language { get; init; } = "vi";
}

public sealed record GenerateQuizRequest
{
    public AiGenerationSnapshot? Snapshot { get; init; }
    public string RequestId { get; init; } = string.Empty;
    public StoryContentDto Story { get; init; } = new();
    public IReadOnlyList<GeneratedVocabularyItemDto> Vocabulary { get; init; } = [];
    public string AgeBand { get; init; } = string.Empty;
    public string Language { get; init; } = "vi";
    public string? ComprehensionGoal { get; init; }
    public decimal? ComprehensionThresholdPercent { get; init; }
}

public sealed record GenerateDiscussionRequest
{
    public AiGenerationSnapshot? Snapshot { get; init; }
    public string RequestId { get; init; } = string.Empty;
    public StoryContentDto Story { get; init; } = new();
    public string AgeBand { get; init; } = string.Empty;
    public string Language { get; init; } = "vi";
    public string? ComprehensionGoal { get; init; }
}

public sealed record EvaluateContentSafetyRequest
{
    public AiGenerationSnapshot? Snapshot { get; init; }
    public string RequestId { get; init; } = string.Empty;
    public StoryContentDto Story { get; init; } = new();
    public string AgeBand { get; init; } = string.Empty;
    public string Language { get; init; } = "vi";
    public IReadOnlyList<string> BlockedTopics { get; init; } = [];
    public IReadOnlyList<string> RestrictedTopics { get; init; } = [];
}
