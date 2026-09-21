using StoryPlatform.Contracts.AI.Models;

namespace StoryPlatform.Contracts.AI.Responses;

public sealed record GenerateOutlineResponse
{
    public string RequestId { get; init; } = string.Empty;
    public string GenerationId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public StoryOutlineDto Outline { get; init; } = new(string.Empty, string.Empty, string.Empty);
    public GenerationMetadataDto Metadata { get; init; } = new();
}

public sealed record GenerateStoryResponse
{
    public string RequestId { get; init; } = string.Empty;
    public string GenerationId { get; init; } = string.Empty;
    public StoryPackageDto Story { get; init; } = new();
    public EvaluationResultDto Evaluation { get; init; } = new();
    public GenerationMetadataDto Metadata { get; init; } = new();
}

public sealed record RefineStoryResponse
{
    public string RequestId { get; init; } = string.Empty;
    public string GenerationId { get; init; } = string.Empty;
    public StoryPackageDto Story { get; init; } = new();
    public EvaluationResultDto Evaluation { get; init; } = new();
    public GenerationMetadataDto Metadata { get; init; } = new();
}

public sealed record EvaluateStoryResponse
{
    public string RequestId { get; init; } = string.Empty;
    public string GenerationId { get; init; } = string.Empty;
    public EvaluationResultDto Evaluation { get; init; } = new();
    public GenerationMetadataDto Metadata { get; init; } = new();
}

public sealed record GenerateStoryContentResponse
{
    public string RequestId { get; init; } = string.Empty;
    public string GenerationId { get; init; } = string.Empty;
    public StoryContentDto Story { get; init; } = new();
    public GenerationMetadataDto Metadata { get; init; } = new();
}

public sealed record RefineStoryContentResponse
{
    public string RequestId { get; init; } = string.Empty;
    public string GenerationId { get; init; } = string.Empty;
    public StoryContentDto Story { get; init; } = new();
    public GenerationMetadataDto Metadata { get; init; } = new();
}

public sealed record GenerateVocabularyResponse
{
    public string RequestId { get; init; } = string.Empty;
    public IReadOnlyList<GeneratedVocabularyItemDto> Items { get; init; } = [];
    public GenerationMetadataDto Metadata { get; init; } = new();
}

public sealed record GenerateQuizResponse
{
    public string RequestId { get; init; } = string.Empty;
    public IReadOnlyList<QuizItemDto> Items { get; init; } = [];
    public GenerationMetadataDto Metadata { get; init; } = new();
}

public sealed record GenerateDiscussionResponse
{
    public string RequestId { get; init; } = string.Empty;
    public IReadOnlyList<DiscussionQuestionDto> Items { get; init; } = [];
    public GenerationMetadataDto Metadata { get; init; } = new();
}

public sealed record EvaluateContentSafetyResponse
{
    public string RequestId { get; init; } = string.Empty;
    public bool IsAllowed { get; init; }
    public bool CanRefine { get; init; }
    public string ReasonCode { get; init; } = "CONTENT_SAFETY_ALLOWED";
    public IReadOnlyList<string> Violations { get; init; } = [];
    public double? SafetyScore { get; init; }
    public GenerationMetadataDto Metadata { get; init; } = new();
}
