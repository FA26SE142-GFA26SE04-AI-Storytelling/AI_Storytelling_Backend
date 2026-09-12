using System;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class StoryGenerationJob : BaseEntity
{
    public int StoryId { get; set; }
    public virtual Story? Story { get; set; }

    public int? PromptCatalogVersionId { get; set; }
    public virtual PromptCatalogVersion? PromptCatalogVersion { get; set; }

    public JobStage Stage { get; set; } = JobStage.InputValidated;
    public GuardrailResult? GuardrailResult { get; set; }
    public string? FallbackMessage { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
