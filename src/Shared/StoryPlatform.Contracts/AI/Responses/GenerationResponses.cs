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
    public string ShortfallReason { get; init; } = string.Empty;
    public string RequestId { get; init; } = string.Empty;
    public IReadOnlyList<GeneratedVocabularyItemDto> Items { get; init; } = [];
    public GenerationMetadataDto Metadata { get; init; } = new();
}

public sealed record GenerateQuizResponse
{
    public string ShortfallReason { get; init; } = string.Empty;
    public string RequestId { get; init; } = string.Empty;
    public IReadOnlyList<QuizItemDto> Items { get; init; } = [];
    public GenerationMetadataDto Metadata { get; init; } = new();
}

/// <summary>The AI fills only the list of the requested interaction type.</summary>
public sealed record GenerateListeningInteractionResponse
{
    public IReadOnlyList<PictureSearchItemDto> PictureSearches { get; init; } = [];
    public IReadOnlyList<TrueFalseItemDto> TrueFalses { get; init; } = [];
    public IReadOnlyList<FollowActionItemDto> FollowActions { get; init; } = [];
    public IReadOnlyList<SentenceBuilderItemDto> SentenceBuilders { get; init; } = [];
    public IReadOnlyList<AlternateReadingItemDto> AlternateReadings { get; init; } = [];
    public IReadOnlyList<InferenceQuestionItemDto> InferenceQuestions { get; init; } = [];
    public IReadOnlyList<CauseEffectItemDto> CauseEffects { get; init; } = [];
    public IReadOnlyList<SceneTitleItemDto> SceneTitles { get; init; } = [];
    public IReadOnlyList<ThoughtBubbleItemDto> ThoughtBubbles { get; init; } = [];
    public IReadOnlyList<CharacterLetterItemDto> CharacterLetters { get; init; } = [];
    public IReadOnlyList<WhoSaidItItemDto> WhoSaidIts { get; init; } = [];
    public string RequestId { get; init; } = string.Empty;
    public IReadOnlyList<CheckpointItemDto> Checkpoints { get; init; } = [];
    public IReadOnlyList<RolePlayItemDto> RolePlays { get; init; } = [];
    public IReadOnlyList<FillBlankItemDto> FillBlanks { get; init; } = [];
    public IReadOnlyList<EventOrderingItemDto> EventOrderings { get; init; } = [];
    public IReadOnlyList<PicturePredictionItemDto> PicturePredictions { get; init; } = [];
    public IReadOnlyList<ReadAloudItemDto> ReadAloudPassages { get; init; } = [];
    public GenerationMetadataDto Metadata { get; init; } = new();
}

public sealed record EvaluateContentSafetyResponse
{
    public string? RiskLevel { get; init; }
    public string? ScoreRuleVersion { get; init; }
    public IReadOnlyList<string> Evidence { get; init; } = [];
    public string RequestId { get; init; } = string.Empty;
    public bool IsAllowed { get; init; }
    public bool CanRefine { get; init; }
    public string ReasonCode { get; init; } = "CONTENT_SAFETY_ALLOWED";
    public IReadOnlyList<string> Violations { get; init; } = [];
    public double? SafetyScore { get; init; }
    public GenerationMetadataDto Metadata { get; init; } = new();
}
