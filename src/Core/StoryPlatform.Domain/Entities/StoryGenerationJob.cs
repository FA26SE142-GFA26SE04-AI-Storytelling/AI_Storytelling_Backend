using System;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class StoryGenerationJob : BaseEntity
{
    public int StoryId { get; set; }
    public virtual Story? Story { get; set; }

    public int? PromptCatalogVersionId { get; set; }
    public virtual PromptCatalogVersion? PromptCatalogVersion { get; set; }

    public int? GenerationRequestId { get; set; }
    public virtual StoryGenerationRequest? GenerationRequest { get; set; }

    public int? StoryVersionId { get; set; }
    public virtual StoryVersion? StoryVersion { get; set; }

    public int? BaseStoryVersionId { get; set; }
    public virtual StoryVersion? BaseStoryVersion { get; set; }

    public int? RequestedByUserId { get; set; }
    public virtual UserAccount? RequestedByUser { get; set; }
    public string? OperationKey { get; set; }

    public GenerationJobOperation Operation { get; set; } = GenerationJobOperation.GenerateOutline;
    public JobStage Stage { get; set; } = JobStage.InputValidated;
    public GenerationJobStatus Status { get; set; } = GenerationJobStatus.Pending;
    public int AttemptNo { get; set; }
    public int MaxAttempts { get; set; } = 3;
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime? LeaseExpiresAt { get; set; }
    public GuardrailResult? GuardrailResult { get; set; }
    public string? FallbackMessage { get; set; }
    public string? ErrorCode { get; set; }
    public string? GenerationMetadataJson { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
