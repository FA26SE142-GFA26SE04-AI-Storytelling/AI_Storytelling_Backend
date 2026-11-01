namespace StoryPlatform.Application.Features.StoryReview.DTOs;

#region Review Package

public sealed record ReviewPackageDto
{
    public string? AssistanceRevisionToken { get; init; }
    public int StoryId { get; init; }
    public string OutputMode { get; init; } = "learning";
    public int StoryVersionId { get; init; }
    public string StoryStatus { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public string Lesson { get; init; } = string.Empty;
    public string ReadabilityAlgorithm { get; init; } = string.Empty;
    public decimal? ReadabilityFkgl { get; init; }
    public decimal? ReadabilityFre { get; init; }

    // Artifact statuses
    public ArtifactStatusDto Vocabulary { get; init; } = new();
    public ArtifactStatusDto Quiz { get; init; } = new();

    // Permissions
    public bool CanEdit { get; init; }
    public bool CanApprove { get; init; }
    public bool CanConfirmContent { get; init; }
    public bool CanArchive { get; init; }
}

public sealed record ArtifactStatusDto
{
    public string State { get; init; } = "pending";
    public int ItemCount { get; init; }
}

#endregion

#region Story Review

public sealed record StoryReviewDto
{
    public int StoryId { get; init; }
    public string OutputMode { get; init; } = "learning";
    public int VersionId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public string Lesson { get; init; } = string.Empty;
    public string ReadabilityAlgorithm { get; init; } = string.Empty;
    public decimal? ReadabilityFkgl { get; init; }
    public decimal? ReadabilityFre { get; init; }
}

public sealed record UpdateStoryRequestDto
{
    public StoryPlatform.Application.Features.CreatorAssistance.DTOs.AssistanceReference[]? AssistanceReferences { get; init; }
    public int VersionId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public string Lesson { get; init; } = string.Empty;
}

public sealed record PartialEditRequestDto
{
    public int VersionId { get; init; }
    public TextSelectionDto Selection { get; init; } = new();
    public string Instruction { get; init; } = string.Empty;
}

public sealed record TextSelectionDto
{
    public int Start { get; init; }
    public int EndExclusive { get; init; }
    public string Text { get; init; } = string.Empty;
}

#endregion

#region Vocabulary Review

public sealed record VocabularyReviewDto
{
    public int StoryId { get; init; }
    public int VersionId { get; init; }
    public List<VocabularyItemDto> Items { get; init; } = new();
}

public sealed record VocabularyItemDto
{
    public string? SourceQuote { get; init; }
    public string? Example { get; init; }
    public int? SourceStart { get; init; }
    public int? SourceEnd { get; init; }
    public int? Id { get; init; }
    public string Term { get; init; } = string.Empty;
    public string Definition { get; init; } = string.Empty;
}

public sealed record UpdateVocabularyRequestDto
{
    public int VersionId { get; init; }
    public List<VocabularyItemDto> Items { get; init; } = new();
}

#endregion

#region Quiz Review

public sealed record QuizReviewDto
{
    public int StoryId { get; init; }
    public int VersionId { get; init; }
    public List<QuizItemDto> Items { get; init; } = new();
}

public sealed record QuizItemDto
{
    public string? SourceQuote { get; init; }
    public string? Explanation { get; init; }
    public string? Hint { get; init; }
    public string? LearningGoal { get; init; }
    public List<string>? OptionFeedback { get; init; }
    public int? SourceStart { get; init; }
    public int? SourceEnd { get; init; }
    public int? Id { get; init; }
    public string Type { get; init; } = string.Empty;
    public string Question { get; init; } = string.Empty;
    public string? CorrectAnswer { get; init; }
    public List<string>? Choices { get; init; }
}

public sealed record UpdateQuizRequestDto
{
    public int VersionId { get; init; }
    public List<QuizItemDto> Items { get; init; } = new();
}

#endregion

#region Validation

public sealed record ValidationResultDto
{
    public bool CanApprove { get; init; }
    public bool CanConfirmContent { get; init; }
    public List<ValidationCheckDto> Checks { get; init; } = new();
    public List<string> Issues { get; init; } = new();
}

public sealed record ValidationCheckDto
{
    public string Name { get; init; } = string.Empty;
    public bool Passed { get; init; }
    public string? Message { get; init; }
}

#endregion

#region Approval

public sealed record ConfirmCreatorContentRequestDto
{
    [System.ComponentModel.DataAnnotations.Range(1, int.MaxValue)]
    public int VersionId { get; init; }
}

public sealed record ConfirmCreatorContentResponseDto
{
    public int StoryId { get; init; }
    public int VersionId { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime ConfirmedAt { get; init; }
}

public sealed record ApproveResponseDto
{
    public bool Success { get; init; }
    public int StoryId { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime ApprovedAt { get; init; }
}

public sealed record ArchiveRequestDto
{
    public string? Reason { get; init; }
}

public sealed record ArchiveResponseDto
{
    public bool Success { get; init; }
    public int StoryId { get; init; }
    public string Status { get; init; } = string.Empty;
}

#endregion

#region AI Proposal

public sealed record AIProposalDto
{
    public string ProposalId { get; init; } = string.Empty;
    public int RequestedByUserId { get; init; }
    public int StoryId { get; init; }
    public int StoryVersionId { get; init; }
    public string ArtifactType { get; init; } = string.Empty;
    public string OperationType { get; init; } = string.Empty;
    public object? OriginalContent { get; init; }
    public object? SuggestedContent { get; init; }
    public string Status { get; init; } = "pending";
    public DateTime CreatedAt { get; init; }
    public DateTime ExpiresAt { get; init; }
}

public sealed record CreateProposalResponseDto
{
    public string ProposalId { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public sealed record ApplyDiscardResponseDto
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
}

#endregion

#region Learning Artifacts Generation

public sealed record GenerateArtifactsRequestDto
{
    public int? StoryVersionId { get; init; }
    public bool IncludeVocabulary { get; init; } = true;
    public bool IncludeQuiz { get; init; } = true;
}

public sealed record ArtifactGenerationResultDto
{
    public int StoryId { get; init; }
    public int StoryVersionId { get; init; }
    public int VocabularyCount { get; init; }
    public int QuizCount { get; init; }
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
}

#endregion
