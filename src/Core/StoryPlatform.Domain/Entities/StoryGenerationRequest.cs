using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

/// <summary>
/// Tracks Phase 1 input validation. Raw creative text is persisted only after Allow.
/// </summary>
public class StoryGenerationRequest : BaseEntity
{
    public int StoryId { get; set; }
    public virtual Story? Story { get; set; }

    public int SubmittedByUserId { get; set; }
    public virtual UserAccount? SubmittedByUser { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;
    public string InputFingerprint { get; set; } = string.Empty;
    public string ContextFingerprint { get; set; } = string.Empty;
    public string ContextSnapshotJson { get; set; } = string.Empty;
    public string? AcceptedInputJson { get; set; }

    public GenerationInputStatus Status { get; set; } = GenerationInputStatus.PendingInput;
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; } = 3;
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
    public string? LastRetryKey { get; set; }
    public DateTime? AttemptStartedAt { get; set; }

    public string? GuardrailDecision { get; set; }
    public string? ReasonCode { get; set; }
    public string? FallbackMessage { get; set; }
    public bool CanRetry { get; set; }
    public string? GuardrailCheckVersion { get; set; }
    public DateTime? GuardrailCheckedAt { get; set; }

    public int? HandoffJobId { get; set; }
    public virtual StoryGenerationJob? HandoffJob { get; set; }
    public DateTime? HandoffCreatedAt { get; set; }
}
