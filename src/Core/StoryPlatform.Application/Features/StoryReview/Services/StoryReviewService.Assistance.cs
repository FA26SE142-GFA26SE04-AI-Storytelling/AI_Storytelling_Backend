using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StoryPlatform.Application.Features.CreatorAssistance;
using StoryPlatform.Application.Features.CreatorAssistance.DTOs;
using StoryPlatform.Application.Features.CreatorAssistance.Services;
using StoryPlatform.Application.Features.ParentAuthoring;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.StoryReview.Services;

public sealed partial class StoryReviewService
{
    private async Task<string?> ValidateReviewAssistanceAsync(int user, Story story, int versionId,
        AssistanceReference[]? references, CancellationToken ct)
    {
        if (references is not { Length: > 0 }) return null;
        if (references.Length > 20 || references.Any(r => r is null) || references.Distinct().Count() != references.Length)
            throw new AssistanceException("INVALID_ASSISTANCE_REFERENCES", 400);
        IAssistanceAccessPolicy policy = story.Scope == StoryScope.Parent
            ? new ParentAssistanceAccessPolicy(_unitOfWork, new ChildProfileAccessGuard(_unitOfWork))
            : new CreatorAssistanceAccessPolicy(_unitOfWork);
        await policy.AuthorizeAsync(user, story, ct);
        await policy.EnsureWritableAsync(story, ct);
        var provenance = new List<AssistanceProvenanceReference>();
        foreach (var reference in references)
        {
            var task = await _unitOfWork.Repository<StoryAssistanceTask>().FirstOrDefaultAsync(
                x => x.Id == reference.TaskId && x.StoryId == story.Id && x.OwnerUserId == user && !x.IsDeleted, cancellationToken: ct);
            var step = await _unitOfWork.Repository<StoryAssistanceStep>().FirstOrDefaultAsync(
                x => x.Id == reference.StepId && x.TaskId == reference.TaskId && !x.IsDeleted, cancellationToken: ct);
            if (task is null || step is null || step.Status != "Succeeded" || reference.Usage is not ("DirectionUsed" or "TrialUsed") ||
                (reference.Usage == "TrialUsed" ? step.Kind != "Trial" : step.Kind != "Direction"))
                throw new AssistanceException("INVALID_ASSISTANCE_REFERENCES", 400);
            if (task.SourceVersionId != versionId || task.SourceRevisionToken != story.AuthoringRevisionToken || task.AudienceRevision != story.AudienceRevision)
                throw new AssistanceException("EDITOR_CONTEXT_STALE", 409);
            await policy.ValidateContextAsync(story, AssistanceContextStorage.ServerContext(task.ContextJson), ct);
            provenance.Add(new(task.Id, step.Id, reference.Usage, task.SnapshotHash,
                step.OutputJson is null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(step.OutputJson))),
                step.Provider, step.Model, step.PromptVersion));
        }
        return JsonSerializer.Serialize(new AssistanceProvenance(1, provenance.ToArray(), versionId, null), ProposalJsonOptions);
    }
}
