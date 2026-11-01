using StoryPlatform.Application.Features.AIStoryInput.Models;
using StoryPlatform.Application.Features.ListeningInteractions.Models;
using StoryPlatform.Application.Features.ListeningInteractions.Services;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ContentGeneration.Services;

/// <summary>Phase 4: sinh tương tác lúc nghe. Mỗi loại trong kế hoạch là một job, chạy lần lượt sau Quiz.</summary>
public sealed partial class ContentGenerationService
{
    private static readonly StoryBlockParser ListeningBlockParser = new();

    private async Task ProcessListeningAsync(int jobId, string claimedToken, CancellationToken cancellationToken)
    {
        var state = await LoadArtifactStateAsync(jobId, GenerationJobOperation.GenerateListeningInteraction, cancellationToken);
        var type = state.Job.ListeningInteractionType ?? throw new InvalidOperationException("INVALID_PHASE3_HANDOFF");
        var versionId = state.Version.Id;
        if (!await _unitOfWork.Repository<StoryVocabulary>().ExistsAsync(x => x.StoryVersionId == versionId, cancellationToken) ||
            !await _unitOfWork.Repository<QuizItem>().ExistsAsync(x => x.StoryVersionId == versionId, cancellationToken))
            throw new InvalidOperationException("INVALID_PHASE3_HANDOFF");
        var scope = ListeningInteractionGenerator.CreateScope(state.Version, state.Context);
        if (scope.Plan.Items.All(x => x.Type != type)) throw new InvalidOperationException("INVALID_PHASE3_HANDOFF");
        IReadOnlyList<ListeningInteractionDraft> drafts = [];
        var metadata = new GenerationMetadataDto();
        if (!await HasListeningTypeAsync(versionId, type, cancellationToken))
            (drafts, metadata) = await _listeningGenerator.GenerateAsync(scope, type, $"listening-job-{state.Job.Id}", cancellationToken);

        await CompleteArtifactAsync(state, claimedToken, metadata, async () =>
        {
            if (await HasListeningTypeAsync(versionId, type, cancellationToken)) return;
            await _listeningGenerator.InsertAsync(versionId, drafts, cancellationToken);
        }, ListeningNext(scope.Plan, type), cancellationToken);
    }

    private Task<bool> HasListeningTypeAsync(int versionId, ListeningInteractionType type, CancellationToken cancellationToken) =>
        _unitOfWork.Repository<ListeningInteraction>().ExistsAsync(
            x => x.StoryVersionId == versionId && x.Type == type && !x.IsDeleted, cancellationToken);

    private static ListeningInteractionPlan ListeningPlan(AIStoryInputContextSnapshot context, IReadOnlyList<StoryTextBlock> blocks) =>
        ListeningInteractionPlanner.Create(ListeningInteractionGenerator.ToAgeBand(context.AgeBand), blocks);

    /// <summary>Job kế tiếp sau loại after (null = ngay sau Quiz); null khi đã hết kế hoạch, truyện sang ContentReview.</summary>
    private static NextArtifact? ListeningNext(ListeningInteractionPlan plan, ListeningInteractionType? after)
    {
        var index = after is null ? 0 : plan.Items.Select(x => x.Type).ToList().IndexOf(after.Value) + 1;
        return index < plan.Items.Count ? new(GenerationJobOperation.GenerateListeningInteraction, plan.Items[index].Type) : null;
    }


}

