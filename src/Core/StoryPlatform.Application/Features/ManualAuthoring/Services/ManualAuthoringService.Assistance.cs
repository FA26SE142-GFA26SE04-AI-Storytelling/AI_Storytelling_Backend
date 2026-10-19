using StoryPlatform.Application.Features.CreatorAuthoring;
using System.Text.Json;
using StoryPlatform.Application.Features.CreatorAssistance;
using StoryPlatform.Application.Features.CreatorAssistance.DTOs;
using StoryPlatform.Application.Features.CreatorAuthoring.DTOs;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.ManualAuthoring.Services;

public partial class ManualAuthoringService
{
    private async Task<string?> ValidateAssistanceAsync(int user, Story story, SaveCreatorContentRequest request, CancellationToken ct)
    {
        if (request.AssistanceReferences is not { Length: > 0 }) return null;
        if (request.AssistanceReferences.Length > 20 || request.AssistanceReferences.Distinct().Count() != request.AssistanceReferences.Length)
            throw new AssistanceException("INVALID_ASSISTANCE_REFERENCES", 400);
        var references = new List<AssistanceProvenanceReference>();
        foreach (var reference in request.AssistanceReferences)
        {
            if (reference is null) throw new AssistanceException("INVALID_ASSISTANCE_REFERENCES", 400);
            var task = await _uow.Repository<StoryAssistanceTask>().FirstOrDefaultAsync(x => x.Id == reference.TaskId &&
                x.OwnerUserId == user && x.StoryId == story.Id && !x.IsDeleted, cancellationToken: ct);
            var step = await _uow.Repository<StoryAssistanceStep>().FirstOrDefaultAsync(x => x.Id == reference.StepId &&
                x.TaskId == reference.TaskId && !x.IsDeleted, cancellationToken: ct);
            if (task is null || step is null || step.Status != "Succeeded" ||
                (reference.Usage != "DirectionUsed" && reference.Usage != "TrialUsed") ||
                (reference.Usage == "DirectionUsed" ? step.Kind != "Direction" : step.Kind != "Trial"))
                throw new AssistanceException("INVALID_ASSISTANCE_REFERENCES", 400);
            await _policy.ValidateAssistanceContextAsync(story,
                StoryPlatform.Application.Features.CreatorAssistance.Services.AssistanceContextStorage.ServerContext(task.ContextJson), ct);
            if (task.AudienceRevision != story.AudienceRevision) throw new AssistanceException("AUDIENCE_CONTEXT_STALE");
            references.Add(new(task.Id, step.Id, reference.Usage, task.SnapshotHash,
                step.OutputJson is null ? null : Hash(System.Text.Encoding.UTF8.GetBytes(step.OutputJson)), step.Provider, step.Model, step.PromptVersion));
        }
        return JsonSerializer.Serialize(new AssistanceProvenance(1, references.ToArray(), request.BaseVersionId, null), JsonOptions);
    }

    private static string? RestoreAssistance(StoryVersion source) => source.AssistanceProvenanceJson is null ? null :
        JsonSerializer.Serialize(new AssistanceProvenance(1, [], null, source.Id,
            // Keep a bounded summary, rather than recursively copying the whole restore ancestry.
            JsonSerializer.Serialize(JsonSerializer.Deserialize<AssistanceProvenance>(source.AssistanceProvenanceJson, JsonOptions)! with { RestoredSourceSummary = null }, JsonOptions)), JsonOptions);
}
