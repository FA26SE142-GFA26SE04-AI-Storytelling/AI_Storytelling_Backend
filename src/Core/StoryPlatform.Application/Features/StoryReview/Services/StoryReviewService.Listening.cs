using System.Text.Json;
using System.Text.Json.Serialization;
using StoryPlatform.Application.Common;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ListeningInteractions.Models;
using StoryPlatform.Application.Features.ListeningInteractions.Services;
using StoryPlatform.Application.Features.StoryReview.DTOs;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.StoryReview.Services;

/// <summary>Duyệt tương tác lúc nghe (spec mục 6.1). Gói phải được người duyệt bấm xác nhận thì truyện mới được duyệt.</summary>
public sealed partial class StoryReviewService
{
    /// <summary>Required = truyện AI ở chế độ Learning có từ 2 đoạn. Thiếu ngữ cảnh sinh thì Error có mã và gói coi như không đạt.</summary>
    private sealed record ListeningReviewState(bool Required, ListeningScope? Scope, ListeningInspection? Inspection, string? Error)
    {
        public bool Valid => !Required || Inspection is { Valid: true };
    }

    public async Task<ListeningReviewDto> GetListeningForReviewAsync(int userId, int storyId, CancellationToken cancellationToken = default)
    {
        var (story, version) = await LoadListeningTargetAsync(userId, storyId, cancellationToken);
        return ToListeningReviewDto(storyId, version.Id, await InspectListeningAsync(story, version, cancellationToken));
    }

    public Task<ListeningReviewDto> UpdateListeningInteractionAsync(
        int userId, int storyId, int interactionId, UpdateListeningInteractionRequestDto input, CancellationToken cancellationToken = default) =>
        CreatorPublicationGuard.WriteAsync(_unitOfWork, storyId, input.VersionId,
            () => UpdateListeningInteractionCoreAsync(userId, storyId, interactionId, input, cancellationToken), cancellationToken);

    public async Task<bool> CompleteListeningReviewAsync(int userId, int storyId, CancellationToken cancellationToken = default)
    {
        var (story, version) = await LoadListeningTargetAsync(userId, storyId, cancellationToken);
        var state = await InspectListeningAsync(story, version, cancellationToken);
        if (!state.Required) return true;
        if (!state.Valid) throw new ConflictException("LISTENING_PACKAGE_INVALID");
        _reviewCompletionStore?.MarkListeningReviewed(storyId, version.Id, userId);
        return true;
    }

    private async Task<ListeningReviewDto> UpdateListeningInteractionCoreAsync(
        int userId, int storyId, int interactionId, UpdateListeningInteractionRequestDto input, CancellationToken cancellationToken)
    {
        if (input.ExtensionData is { Count: > 0 } || input.Options?.Any(o => o.ExtensionData is { Count: > 0 }) == true)
            throw new BadRequestException("FIELD_NOT_EDITABLE");
        var (story, version) = await LoadListeningTargetAsync(userId, storyId, cancellationToken);
        if (version.Id != input.VersionId) throw new ConflictException("STALE_STORY_VERSION");
        var interactionRepo = _unitOfWork.Repository<ListeningInteraction>();
        var optionRepo = _unitOfWork.Repository<ListeningInteractionOption>();
        var interaction = await interactionRepo.FirstOrDefaultAsync(
                x => x.Id == interactionId && x.StoryVersionId == version.Id && !x.IsDeleted, cancellationToken: cancellationToken)
            ?? throw new NotFoundException("ListeningInteraction", interactionId);
        var options = (await optionRepo.FindAsync(
                x => x.ListeningInteractionId == interaction.Id && !x.IsDeleted, cancellationToken: cancellationToken))
            .OrderBy(x => x.SortOrder).ToList();
        var changes = input.Options ?? [];
        if (changes.Select(x => x.Id).Distinct().Count() != changes.Count || changes.Any(c => options.All(o => o.Id != c.Id)))
            throw new BadRequestException("LISTENING_OPTION_NOT_FOUND");

        var scope = await _listening.LoadScopeAsync(story, version, cancellationToken);
        var planItem = scope.Plan.Items.FirstOrDefault(p => p.Type == interaction.Type)
            ?? throw new ConflictException("LISTENING_TYPE_NOT_IN_PLAN");
        var current = ListeningInteractionMapper.ToDraft(interaction, options, scope.Blocks);
        var byId = changes.ToDictionary(x => x.Id);
        // Áp lên draft để chạy luật trước; không đạt thì không ghi gì vào entity.
        var draft = current with
        {
            Prompt = Edited(input.Prompt) ?? current.Prompt,
            Hint = Edited(input.Hint) ?? current.Hint,
            Explanation = Edited(input.Explanation) ?? current.Explanation,
            MatchFeedback = Edited(input.MatchFeedback) ?? current.MatchFeedback,
            NoMatchFeedback = Edited(input.NoMatchFeedback) ?? current.NoMatchFeedback,
            WaitFeedback = Edited(input.WaitFeedback) ?? current.WaitFeedback,
            Options = current.Options.Select((o, i) => byId.TryGetValue(options[i].Id, out var c)
                ? o with { Text = Edited(c.Text) ?? o.Text, Feedback = Edited(c.Feedback) ?? o.Feedback }
                : o).ToArray()
        };
        var errors = new List<ListeningValidationError>(EmptyEdits(input));
        errors.AddRange(ListeningInteractionRules.ValidateText(draft, 0, _listening.RuleContext(scope, planItem, [])));
        if (errors.Count > 0)
            throw new BadRequestException("LISTENING_TEXT_INVALID:" +
                string.Join(';', errors.Distinct().Select(e => e.Field is null ? e.Code : $"{e.Code}@{e.Field}")));

        interaction.Prompt = draft.Prompt;
        interaction.Hint = draft.Hint;
        interaction.Explanation = draft.Explanation;
        interaction.MatchFeedback = draft.MatchFeedback;
        interaction.NoMatchFeedback = draft.NoMatchFeedback;
        interaction.WaitFeedback = draft.WaitFeedback;
        interactionRepo.Update(interaction);
        for (var i = 0; i < options.Count; i++)
        {
            options[i].Text = draft.Options[i].Text;
            options[i].Feedback = draft.Options[i].Feedback;
            optionRepo.Update(options[i]);
        }
        await _listening.RetireMediaAsync([interaction.Id], options.Select(x => x.Id).ToArray(), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _reviewCompletionStore?.ClearListeningReviewed(storyId, version.Id);
        return ToListeningReviewDto(storyId, version.Id, await InspectListeningAsync(story, version, cancellationToken));
    }

    private static string? Edited(string? value) => value?.Trim();

    // Gửi chuỗi rỗng là xoá chữ; không cho, vì FE dùng null để giữ nguyên.
    private static IEnumerable<ListeningValidationError> EmptyEdits(UpdateListeningInteractionRequestDto input)
    {
        (string Field, string? Value)[] fields =
        [
            ("question", input.Prompt), ("hint", input.Hint), ("explanation", input.Explanation),
            ("matchFeedback", input.MatchFeedback), ("noMatchFeedback", input.NoMatchFeedback), ("waitFeedback", input.WaitFeedback)
        ];
        foreach (var (field, value) in fields)
            if (value is not null && string.IsNullOrWhiteSpace(value)) yield return new("FIELD_EMPTY", null, field);
        foreach (var option in input.Options ?? [])
        {
            if (option.Text is not null && string.IsNullOrWhiteSpace(option.Text)) yield return new("FIELD_EMPTY", null, $"options[{option.Id}].text");
            if (option.Feedback is not null && string.IsNullOrWhiteSpace(option.Feedback)) yield return new("FIELD_EMPTY", null, $"options[{option.Id}].feedback");
        }
    }

    private async Task<(Story Story, StoryVersion Version)> LoadListeningTargetAsync(int userId, int storyId, CancellationToken ct)
    {
        await AuthorizeCreatorOperationAsync(userId, storyId, ct);
        await EnsureLearningModeAsync(userId, storyId, ct); // gồm kiểm quyền duyệt (chủ hồ sơ trẻ hoặc creator)
        var story = await _storyRepo.GetByIdAsync(storyId, ct) ?? throw new NotFoundException("Story", storyId);
        var version = await _versionRepo.FirstOrDefaultAsync(v => v.StoryId == storyId && v.IsCurrent, cancellationToken: ct)
            ?? throw new NotFoundException("StoryVersion");
        return (story, version);
    }

    private async Task<ListeningReviewState> InspectListeningAsync(Story story, StoryVersion version, CancellationToken ct)
    {
        // Truyện có sẵn (luồng 2) và ReadingMediaOnly không có tương tác; truyện 1 đoạn có kế hoạch rỗng.
        if (story.Source != StorySource.Ai || story.OutputMode != StoryOutputMode.Learning ||
            !ListeningInteractionGenerator.HasPlan(version.Content))
            return new(false, null, null, null);
        ListeningScope scope;
        try { scope = await _listening.LoadScopeAsync(story, version, ct); }
        catch (ConflictException ex) { return new(true, null, null, ex.Message); }
        return new(true, scope, await _listening.InspectAsync(scope, ct), null);
    }

    private ListeningReviewDto ToListeningReviewDto(int storyId, int versionId, ListeningReviewState state) => new()
    {
        StoryId = storyId,
        VersionId = versionId,
        Required = state.Required,
        Reviewed = _reviewCompletionStore?.IsListeningReviewed(storyId, versionId) ?? false,
        Plan = state.Scope?.Plan.Items.Select(p => new ListeningPlanItemDto { Type = p.Type.ToString(), Count = p.Count, Zones = p.Zones }).ToList() ?? [],
        Items = state.Inspection?.Items.Select(ToListeningItemDto).ToList() ?? [],
        Issues = ListeningIssues(state).ToList()
    };

    private static IEnumerable<ListeningIssueDto> ListeningIssues(ListeningReviewState state) =>
        state.Error is not null
            ? [new ListeningIssueDto { Code = state.Error }]
            : state.Inspection?.Issues.Select(i => new ListeningIssueDto
                { Code = i.Code, Type = i.Type?.ToString(), ItemIndex = i.ItemIndex, Field = i.Field }) ?? [];

    private static ListeningInteractionReviewDto ToListeningItemDto(ListeningSavedItem item) => new()
    {
        Id = item.Interaction.Id,
        Type = item.Interaction.Type.ToString(),
        AnchorBlockId = item.Draft.AnchorBlockId,
        AnchorOffset = item.Interaction.AnchorOffset,
        Kind = item.Interaction.Kind?.ToString(),
        Prompt = item.Interaction.Prompt,
        SourceQuote = item.Interaction.SourceQuote,
        CharacterName = item.Interaction.CharacterName,
        HiddenWord = item.Interaction.HiddenWord,
        Hint = item.Interaction.Hint,
        RevealBlockId = item.Draft.RevealBlockId,
        Explanation = item.Interaction.Explanation,
        MatchFeedback = item.Interaction.MatchFeedback,
        NoMatchFeedback = item.Interaction.NoMatchFeedback,
        WaitFeedback = item.Interaction.WaitFeedback,
        IsStale = item.IsStale,
        Options = item.Options.Select((o, i) => new ListeningOptionReviewDto
        {
            Id = o.Id,
            SortOrder = o.SortOrder,
            Text = o.Text,
            Feedback = o.Feedback,
            IsMatch = o.IsMatch,
            CorrectOrder = o.CorrectOrder,
            BlockId = item.Draft.Options[i].BlockId,
            SourceQuote = o.SourceQuote,
            ImageDescription = o.ImageDescription
        }).ToList()
    };
    private const string ListeningArtifactPrefix = "listening:";
    // Enum ghi bằng tên để FE xem trước được; đọc lại cùng options này.
    private static readonly JsonSerializerOptions ListeningProposalJsonOptions =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public async Task<CreateProposalResponseDto> CreateListeningProposalAsync(
        int userId, int storyId, ListeningInteractionType type, CancellationToken cancellationToken = default)
    {
        var (story, version) = await LoadListeningTargetAsync(userId, storyId, cancellationToken);
        if (story.Source != StorySource.Ai) throw new ConflictException("LISTENING_NOT_SUPPORTED");
        var scope = await _listening.LoadScopeAsync(story, version, cancellationToken);
        IReadOnlyList<ListeningInteractionDraft> drafts;
        try
        {
            drafts = (await _listening.GenerateAsync(scope, type, $"listening-review-{storyId}-{Guid.NewGuid():N}", cancellationToken)).Drafts;
        }
        catch (InvalidOperationException ex) when (ex.Message.EndsWith("_VALIDATION_FAILED", StringComparison.Ordinal))
        {
            throw new ConflictException(ex.Message);
        }

        var proposal = new AIProposalDto
        {
            RequestedByUserId = userId,
            StoryId = storyId,
            StoryVersionId = version.Id,
            ArtifactType = ListeningArtifactPrefix + type,
            OperationType = "regenerate",
            OriginalContent = JsonSerializer.SerializeToElement(new { }),
            SuggestedContent = JsonSerializer.SerializeToElement(new { items = drafts }, ListeningProposalJsonOptions),
            Status = "pending",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
        var proposalId = await _proposalCache.CreateAsync(proposal);
        return new CreateProposalResponseDto { ProposalId = proposalId, Message = "Đã tạo đề xuất tương tác lúc nghe để xem trước." };
    }

    public Task ApplyListeningProposalAsync(int userId, AIProposalDto proposal, CancellationToken cancellationToken = default) =>
        CreatorPublicationGuard.WriteAsync(_unitOfWork, proposal.StoryId, proposal.StoryVersionId, async () =>
        {
            await ApplyListeningProposalCoreAsync(userId, proposal, cancellationToken);
            return true;
        }, cancellationToken);

    private async Task ApplyListeningProposalCoreAsync(int userId, AIProposalDto proposal, CancellationToken cancellationToken)
    {
        if (!proposal.ArtifactType.StartsWith(ListeningArtifactPrefix, StringComparison.Ordinal) ||
            !Enum.TryParse<ListeningInteractionType>(proposal.ArtifactType[ListeningArtifactPrefix.Length..], out var type))
            throw new BadRequestException($"Unknown artifact type: {proposal.ArtifactType}");
        var (story, version) = await LoadListeningTargetAsync(userId, proposal.StoryId, cancellationToken);
        if (version.Id != proposal.StoryVersionId) throw new ConflictException("STALE_PROPOSAL_VERSION");
        var drafts = proposal.SuggestedContent is JsonElement element && element.TryGetProperty("items", out var items)
            ? items.Deserialize<ListeningInteractionDraft[]>(ListeningProposalJsonOptions) ?? []
            : [];
        var scope = await _listening.LoadScopeAsync(story, version, cancellationToken);
        var planItem = scope.Plan.Items.FirstOrDefault(p => p.Type == type) ?? throw new ConflictException("LISTENING_TYPE_NOT_IN_PLAN");
        // Gói có thể đã đổi từ lúc tạo đề xuất (loại khác vừa được thay): kiểm lại với neo đang chiếm hiện tại.
        var occupied = (await _listening.LoadSavedAsync(version.Id, cancellationToken))
            .Where(x => x.Type != type).Select(x => x.AnchorOffset).ToArray();
        var invalid = drafts.SelectMany((d, i) => ListeningInteractionRules.CheckStale(d, i, version.Content!, scope.Blocks)).Any() ||
                      ListeningInteractionRules.Validate(drafts, _listening.RuleContext(scope, planItem, occupied)).Count > 0;
        if (invalid) throw new ConflictException("LISTENING_PROPOSAL_INVALID");
        await _listening.ReplaceAsync(version.Id, type, drafts, cancellationToken);
        _reviewCompletionStore?.ClearListeningReviewed(proposal.StoryId, version.Id);
    }
}
