using System.ComponentModel.DataAnnotations;

namespace StoryPlatform.Application.Features.AIStoryInput.DTOs;

public sealed class AIStoryInputContextDto
{
    public int ChildProfileId { get; init; }
    public string ChildNickname { get; init; } = string.Empty;
    public string AgeBand { get; init; } = string.Empty;
    public int ReadingLevel { get; init; }
    public string DefaultVocabularyLevel { get; init; } = string.Empty;
    public IReadOnlyList<string> AvailableVocabularyLevels { get; init; } = [];
    public string DefaultLanguage { get; init; } = string.Empty;
    public IReadOnlyList<string> AvailableLanguages { get; init; } = [];
    public int MaximumLength { get; init; }
    public string RequiredApprovalMode { get; init; } = string.Empty;
    public IReadOnlyList<string> Interests { get; init; } = [];
    public IReadOnlyList<string> AllowedCategoryCodes { get; init; } = [];
    public IReadOnlyList<string> RestrictedCategoryCodes { get; init; } = [];
    public IReadOnlyList<string> BlockedCategoryCodes { get; init; } = [];
    public IReadOnlyList<string> CharacterModes { get; init; } = ["specified", "ai_suggested"];
    public IReadOnlyList<string> SettingModes { get; init; } = ["specified", "ai_suggested"];
}

public class AIStoryCreativeInputDto
{
    [Required, StringLength(200, MinimumLength = 2)]
    public string Topic { get; init; } = string.Empty;

    [StringLength(50)]
    public string? Genre { get; init; }

    [Required, RegularExpression("^(specified|ai_suggested)$")]
    public string CharacterMode { get; init; } = "ai_suggested";

    public IReadOnlyList<string> Characters { get; init; } = [];

    [Required, RegularExpression("^(specified|ai_suggested)$")]
    public string SettingMode { get; init; } = "ai_suggested";

    [StringLength(300)]
    public string? Setting { get; init; }

    [Required, StringLength(500, MinimumLength = 2)]
    public string Lesson { get; init; } = string.Empty;

    [Required, RegularExpression("^level_[1-5]$")]
    public string VocabularyLevel { get; init; } = string.Empty;

    [StringLength(10)]
    public string? Language { get; init; }

    [Range(1, 10_000)]
    public int TargetLength { get; init; }
}

public sealed class SubmitAIStoryInputRequestDto : AIStoryCreativeInputDto
{
    [Range(1, int.MaxValue)]
    public int ChildProfileId { get; init; }

    public int? ExistingStoryId { get; init; }

    [Required, StringLength(100, MinimumLength = 8)]
    public string IdempotencyKey { get; init; } = string.Empty;
}

public sealed class RetryAIStoryInputRequestDto : AIStoryCreativeInputDto
{
    [Required, StringLength(100, MinimumLength = 8)]
    public string RetryKey { get; init; } = string.Empty;
}

public sealed class AIStoryInputProgressDto
{
    public int StoryId { get; init; }
    public int RequestId { get; init; }
    public string InputStatus { get; init; } = string.Empty;
    public string HandoffStatus { get; init; } = "none";
    public int? HandoffJobId { get; init; }
    public int AttemptCount { get; init; }
    public int MaxAttempts { get; init; }
    public string? ReasonCode { get; init; }
    public string? FallbackMessage { get; init; }
    public bool CanRetry { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public DateTime? GuardrailCheckedAt { get; init; }
    public DateTime? HandoffCreatedAt { get; init; }
}
