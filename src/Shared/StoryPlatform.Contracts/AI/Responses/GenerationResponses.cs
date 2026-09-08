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
