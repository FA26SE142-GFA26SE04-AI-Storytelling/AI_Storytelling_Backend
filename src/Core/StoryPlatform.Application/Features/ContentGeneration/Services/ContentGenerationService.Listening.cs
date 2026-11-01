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
        var blocks = ListeningBlockParser.Parse(state.Version.Content!);
        var plan = ListeningPlan(state.Context, blocks);
        var item = plan.Items.FirstOrDefault(x => x.Type == type) ?? throw new InvalidOperationException("INVALID_PHASE3_HANDOFF");
        var saved = await _unitOfWork.Repository<ListeningInteraction>().FindAsync(
            x => x.StoryVersionId == versionId, cancellationToken: cancellationToken);
        IReadOnlyList<ListeningInteractionDraft> drafts = [];
        var metadata = new GenerationMetadataDto();
        if (!saved.Any(x => x.Type == type))
            (drafts, metadata) = await GenerateListeningDraftsAsync(state, item, plan, blocks, saved, cancellationToken);

        await CompleteArtifactAsync(state, claimedToken, metadata, async () =>
        {
            if (await _unitOfWork.Repository<ListeningInteraction>().ExistsAsync(
                    x => x.StoryVersionId == versionId && x.Type == type, cancellationToken)) return;
            foreach (var draft in drafts)
            {
                var (interaction, options) = ListeningInteractionMapper.ToEntities(draft, versionId);
                await _unitOfWork.Repository<ListeningInteraction>().AddAsync(interaction, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken); // option cần Id của tương tác
                foreach (var option in options) option.ListeningInteractionId = interaction.Id;
                await _unitOfWork.Repository<ListeningInteractionOption>().AddRangeAsync(options, cancellationToken);
            }
        }, ListeningNext(plan, type), cancellationToken);
    }

    private async Task<(IReadOnlyList<ListeningInteractionDraft> Drafts, GenerationMetadataDto Metadata)> GenerateListeningDraftsAsync(
        ArtifactState state, ListeningPlanItem item, ListeningInteractionPlan plan, IReadOnlyList<StoryTextBlock> blocks,
        IReadOnlyList<ListeningInteraction> saved, CancellationToken cancellationToken)
    {
        var content = state.Version.Content!;
        // Neo của các loại khác đã lưu; loại đang sinh không tự chặn mình.
        var occupied = saved.Where(x => x.Type != item.Type).Select(x => x.AnchorOffset).ToArray();
        var context = new ListeningRuleContext
        {
            Type = item.Type,
            Count = item.Count,
            AgeBand = ToListeningAgeBand(state.Context.AgeBand),
            Content = content,
            Blocks = blocks,
            OccupiedAnchorOffsets = occupied,
            DialogueBlockIds = plan.DialogueBlockIds,
            Zones = item.Zones, // vùng của từng mục (spec mục 4.1)
            GroundingSource = $"{content} {state.Version.Lesson}",
            IsSafe = text => IsArtifactSafe(text, state.Context),
            GradingWords = _listeningGradingWords
        };
        var vocabulary = item.Type == ListeningInteractionType.FillBlank
            ? (await _unitOfWork.Repository<StoryVocabulary>().FindAsync(
                x => x.StoryVersionId == state.Version.Id, cancellationToken: cancellationToken)).Select(x => x.Term).ToArray()
            : [];
        IReadOnlyList<ListeningValidationError> previous = [];
        for (var attempt = 1; ; attempt++)
        {
            var response = await _aiClient.GenerateListeningInteractionAsync(ListeningInteractionAiMapper.BuildRequest(
                $"listening-job-{state.Job.Id}-{item.Type}-attempt-{attempt}", item.Type, item.Count, context.AgeBand, state.Context.Language,
                state.Version.Title, state.Version.Lesson ?? string.Empty, blocks, plan.DialogueBlockIds, occupied, item.Zones, vocabulary, previous),
                cancellationToken);
            var mapped = ListeningInteractionMapper.Resolve(ListeningInteractionAiMapper.ToDrafts(response, item.Type), content, blocks);
            var errors = mapped.Errors.Concat(ListeningInteractionRules.Validate(mapped.Items, context)).Distinct().ToArray();
            if (errors.Length == 0) return (mapped.Items, response.Metadata);
            if (attempt >= _artifactMaxAttempts) throw ListeningFailure(item.Type, errors[0]);
            previous = errors;
        }
    }

    private static ListeningInteractionPlan ListeningPlan(AIStoryInputContextSnapshot context, IReadOnlyList<StoryTextBlock> blocks) =>
        ListeningInteractionPlanner.Create(ToListeningAgeBand(context.AgeBand), blocks);

    /// <summary>Job kế tiếp sau loại after (null = ngay sau Quiz); null khi đã hết kế hoạch, truyện sang ContentReview.</summary>
    private static NextArtifact? ListeningNext(ListeningInteractionPlan plan, ListeningInteractionType? after)
    {
        var index = after is null ? 0 : plan.Items.Select(x => x.Type).ToList().IndexOf(after.Value) + 1;
        return index < plan.Items.Count ? new(GenerationJobOperation.GenerateListeningInteraction, plan.Items[index].Type) : null;
    }

    // Snapshot lưu "6-8" / "9-10"; chấp nhận cả tên enum như LearningBatchOptions.
    private static AgeBand ToListeningAgeBand(string ageBand) => ageBand is "9-10" or "Age_9_10" ? AgeBand.Age_9_10 : AgeBand.Age_6_8;

    private static InvalidOperationException ListeningFailure(ListeningInteractionType type, ListeningValidationError error)
    {
        var failure = new InvalidOperationException($"{ToSnake(type.ToString()).ToUpperInvariant()}_VALIDATION_FAILED");
        failure.Data["ValidationCode"] = error.Code;
        if (error.ItemIndex.HasValue) failure.Data["LearningItemIndex"] = error.ItemIndex.Value;
        return failure;
    }
}

