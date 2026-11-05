using System.Text.Json;
using StoryPlatform.Application.Abstractions.AI;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AIStoryInput.Models;
using StoryPlatform.Application.Features.ContentGeneration;
using StoryPlatform.Application.Features.ContentGeneration.Interfaces;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Contracts.AI.Responses;
using StoryPlatform.Application.Features.ListeningInteractions.Models;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ListeningInteractions.Services;

/// <summary>
/// Sinh, lưu, thay và kiểm tra gói tương tác lúc nghe của một version.
/// Job Phase 4, phần duyệt và artifacts/generate cùng dùng để luật ở mọi chỗ giống nhau.
/// </summary>
public sealed class ListeningInteractionGenerator
{
    private static readonly StoryBlockParser BlockParser = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAIStoryGenerationClient _aiClient;
    private readonly int _maxAttempts;
    private readonly IReadOnlyCollection<string> _gradingWords;
    private readonly ILearningRequestGate? _gate;

    public ListeningInteractionGenerator(IUnitOfWork unitOfWork, IAIStoryGenerationClient aiClient, ContentGenerationOptions? options = null,
        ILearningRequestGate? gate = null)
    {
        options ??= new ContentGenerationOptions();
        _unitOfWork = unitOfWork;
        _aiClient = aiClient;
        _gate = gate;
        _maxAttempts = Math.Clamp(options.ArtifactMaxAttempts, 1, 3);
        _gradingWords = options.ListeningGradingWords is { Length: > 0 } words ? words : ListeningInteractionRules.DefaultGradingWords;
    }

    /// <summary>Kế hoạch chỉ rỗng khi truyện có dưới 2 đoạn; kiểm tra được mà không cần ngữ cảnh sinh.</summary>
    public static bool HasPlan(string? content) => !string.IsNullOrEmpty(content) && BlockParser.Parse(content).Count >= 2;

    public static ListeningScope CreateScope(StoryVersion version, AIStoryInputContextSnapshot context)
    {
        var blocks = BlockParser.Parse(version.Content!);
        return new(version, context, blocks, ListeningInteractionPlanner.Create(ToAgeBand(context.AgeBand), blocks));
    }

    /// <summary>Ngữ cảnh lấy từ yêu cầu sinh đã chấp nhận gần nhất, giống job Phase 4. Thiếu thì đóng chặn.</summary>
    public async Task<ListeningScope> LoadScopeAsync(Story story, StoryVersion version, CancellationToken ct)
    {
        var request = (await _unitOfWork.Repository<StoryGenerationRequest>().FindAsync(
                x => x.StoryId == story.Id && x.Status == GenerationInputStatus.InputAccepted && !x.IsDeleted, cancellationToken: ct))
            .OrderByDescending(x => x.Id).FirstOrDefault() ?? throw new ConflictException("LISTENING_CONTEXT_MISSING");
        AIStoryInputContextSnapshot? context;
        try { context = JsonSerializer.Deserialize<AIStoryInputContextSnapshot>(request.ContextSnapshotJson, JsonOptions); }
        catch (JsonException) { context = null; }
        if (context is null) throw new ConflictException("LISTENING_CONTEXT_MISSING");
        try { GenerationStoryAccess.ValidateSnapshot(story, context); }
        catch (InvalidOperationException) { throw new ConflictException("LISTENING_CONTEXT_MISSING"); }
        return CreateScope(version, context);
    }

    /// <summary>Gọi AI cho một loại, thử lại kèm previousErrors (chỉ mã); hết lượt thì ném &lt;TYPE&gt;_VALIDATION_FAILED.</summary>
    public async Task<ListeningGenerationResult> GenerateAsync(
        ListeningScope scope, ListeningInteractionType type, string requestIdPrefix, CancellationToken ct,
        ListeningJobState? durableState = null, Func<ListeningJobState, CancellationToken, Task>? checkpoint = null)
    {
        var item = scope.Plan.Items.FirstOrDefault(x => x.Type == type) ?? throw new ConflictException("LISTENING_TYPE_NOT_IN_PLAN");
        if (type == ListeningInteractionType.RolePlay && item.Zones.Any(zone => !zone.Any(scope.Plan.DialogueBlockIds.Contains)))
            throw new ConflictException("LISTENING_PLAN_INFEASIBLE");
        // Neo của các loại khác đã lưu; loại đang sinh không tự chặn mình.
        var occupied = (await LoadSavedAsync(scope.Version.Id, ct, scope.ParentStoryPackageId)).Where(x => x.Type != type).Select(x => x.AnchorOffset).ToArray();
        var context = RuleContext(scope, item, occupied);
        var vocabulary = type == ListeningInteractionType.FillBlank
            ? (await _unitOfWork.Repository<StoryVocabulary>().FindAsync(
                x => x.StoryVersionId == scope.Version.Id && x.ParentStoryPackageId == scope.ParentStoryPackageId && !x.IsDeleted, cancellationToken: ct)).Select(x => x.Term).ToArray()
            : [];
        var state = durableState ?? new ListeningJobState { MaximumValidationAttempts = _maxAttempts };
        var fingerprint = ListeningJobState.Fingerprint(scope);
        if (state.PinnedRequest is not null && state.SourceFingerprint != fingerprint)
            throw new InvalidOperationException("LISTENING_SOURCE_STALE");
        if (state.PinnedRequest is null)
        {
            var request = ListeningInteractionAiMapper.BuildRequest(requestIdPrefix, type, item.Count, context.AgeBand, scope.Context.Language,
                scope.Version.Title, scope.Version.Lesson ?? string.Empty, scope.Blocks, scope.Plan.DialogueBlockIds, occupied,
                item.Zones, vocabulary, []) with { ReadingLevel = scope.Context.ReadingLevel };
            // Direct construction without a gate is retained for isolated unit fixtures. Production DI always supplies the shared gate.
            if (_gate is not null || durableState is not null)
            {
                var snapshot = await _aiClient.GetListeningPromptSnapshotAsync(type.ToString(), context.AgeBand.ToString(), scope.Context.Language, ct);
                if (snapshot is null || string.IsNullOrWhiteSpace(snapshot.Version) || string.IsNullOrWhiteSpace(snapshot.PromptType) ||
                    snapshot.Template?.Contains("{{context}}", StringComparison.Ordinal) != true)
                    throw new InvalidOperationException("LISTENING_SNAPSHOT_INVALID");
                request = request with { Snapshot = new AiGenerationSnapshot
                {
                    PromptVersionNo = snapshot.Version,
                    Templates = new Dictionary<string, string> { [snapshot.PromptType] = snapshot.Template }
                } };
            }
            state.PinnedRequest = request;
            state.SourceFingerprint = fingerprint;
            if (checkpoint is not null) await checkpoint(state, ct);
        }
        if (state.PinnedRequest.InteractionType != type.ToString() || state.PinnedRequest.Count != item.Count)
            throw new InvalidOperationException("LISTENING_SOURCE_STALE");
        if (state.ValidationFailures >= state.MaximumValidationAttempts)
            throw Failure(type, new(state.PreviousErrors.FirstOrDefault()?.Code ?? "LISTENING_VALIDATION_FAILED"));
        for (var attempt = 1; ; attempt++)
        {
            using var permit = _gate is null ? null : await _gate.EnterAsync(ct);
            if (state.ProviderCalls >= 3) throw new InvalidOperationException("LISTENING_RETRY_EXHAUSTED");
            state.ProviderCalls++;
            if (checkpoint is not null) await checkpoint(state, ct);
            GenerateListeningInteractionResponse response;
            try
            {
                response = await _aiClient.GenerateListeningInteractionAsync(state.PinnedRequest with
                {
                    RequestId = $"{requestIdPrefix}-{type}-attempt-{state.ProviderCalls}", PreviousErrors = state.PreviousErrors
                }, ct);
            }
            catch (AIServiceRequestException ex) when (ex.ErrorCode == "AI_OUTPUT_INVALID")
            {
                state.ValidationFailures++;
                state.PreviousErrors = [new("SCHEMA_INVALID", null, null)];
                if (checkpoint is not null) await checkpoint(state, ct);
                if (state.ValidationFailures >= state.MaximumValidationAttempts) throw Failure(type, new("SCHEMA_INVALID"));
                continue;
            }
            catch (AIServiceRequestException ex) when (ex.StatusCode == 429)
            {
                _gate?.Defer(ex.RetryAfter is { } after && after > TimeSpan.Zero ? after : TimeSpan.FromSeconds(60));
                throw;
            }
            if (response is null) throw new InvalidOperationException("LISTENING_RESPONSE_INVALID");
            var mapped = ListeningInteractionMapper.Resolve(ListeningInteractionAiMapper.ToDrafts(response, type), scope.Version.Content!, scope.Blocks);
            var errors = mapped.Errors.Concat(ListeningInteractionRules.Validate(mapped.Items, context)).Distinct().ToArray();
            if (errors.Length == 0) return new(mapped.Items, response.Metadata);
            state.ValidationFailures++;
            state.PreviousErrors = errors.Select(e => new ListeningPreviousErrorDto(e.Code, e.ItemIndex, e.Field)).ToArray();
            if (checkpoint is not null) await checkpoint(state, ct);
            if (state.ValidationFailures >= state.MaximumValidationAttempts) throw Failure(type, errors[0]);
        }
    }

    public ListeningRuleContext RuleContext(ListeningScope scope, ListeningPlanItem item, IReadOnlyCollection<int> occupiedAnchorOffsets) => new()
    {
        Type = item.Type,
        Count = item.Count,
        AgeBand = ToAgeBand(scope.Context.AgeBand),
        Content = scope.Version.Content!,
        Blocks = scope.Blocks,
        OccupiedAnchorOffsets = occupiedAnchorOffsets,
        DialogueBlockIds = scope.Plan.DialogueBlockIds,
        Zones = item.Zones, // vùng của từng mục (spec mục 4.1)
        GroundingSource = $"{scope.Version.Content} {scope.Version.Lesson}",
        IsSafe = text => IsSafe(text, scope.Context),
        GradingWords = _gradingWords
    };

    public async Task<IReadOnlyList<ListeningInteraction>> LoadSavedAsync(int versionId, CancellationToken ct, int? parentStoryPackageId = null) =>
        await _unitOfWork.Repository<ListeningInteraction>().FindAsync(
            x => x.StoryVersionId == versionId && x.ParentStoryPackageId == parentStoryPackageId && !x.IsDeleted, cancellationToken: ct);

    /// <summary>Lưu draft đã qua luật. Người gọi lo transaction và lần SaveChanges cuối.</summary>
    public async Task<int> InsertAsync(int versionId, IReadOnlyList<ListeningInteractionDraft> drafts, CancellationToken ct, int? parentStoryPackageId = null)
    {
        foreach (var draft in drafts)
        {
            var (interaction, options) = ListeningInteractionMapper.ToEntities(draft, versionId);
            interaction.ParentStoryPackageId = parentStoryPackageId;
            await _unitOfWork.Repository<ListeningInteraction>().AddAsync(interaction, ct);
            await _unitOfWork.SaveChangesAsync(ct); // option cần Id của tương tác
            foreach (var option in options) option.ListeningInteractionId = interaction.Id;
            await _unitOfWork.Repository<ListeningInteractionOption>().AddRangeAsync(options, ct);
        }
        return drafts.Count;
    }

    /// <summary>Thay toàn bộ dòng của một loại: xoá mềm dòng cũ, tắt media gắn với chúng, rồi thêm dòng mới.</summary>
    public async Task<int> ReplaceAsync(
        int versionId, ListeningInteractionType type, IReadOnlyList<ListeningInteractionDraft> drafts, CancellationToken ct, int? parentStoryPackageId = null)
    {
        var interactionRepo = _unitOfWork.Repository<ListeningInteraction>();
        var optionRepo = _unitOfWork.Repository<ListeningInteractionOption>();
        var old = await interactionRepo.FindAsync(
            x => x.StoryVersionId == versionId && x.ParentStoryPackageId == parentStoryPackageId && x.Type == type && !x.IsDeleted, cancellationToken: ct);
        // List, không dùng mảng: C# 14 gắn int[].Contains vào ReadOnlySpan và EF không dịch được.
        var ids = old.Select(x => x.Id).ToList();
        var options = await optionRepo.FindAsync(x => ids.Contains(x.ListeningInteractionId) && !x.IsDeleted, cancellationToken: ct);
        foreach (var option in options)
        {
            option.IsDeleted = true;
            optionRepo.Update(option);
        }
        foreach (var interaction in old)
        {
            interaction.IsDeleted = true;
            interactionRepo.Update(interaction);
        }
        await RetireMediaAsync(ids, options.Select(x => x.Id).ToList(), ct);
        await _unitOfWork.SaveChangesAsync(ct); // xoá mềm trước để neo cũ không đụng index unique khi thêm mới
        var count = await InsertAsync(versionId, drafts, ct, parentStoryPackageId);
        await _unitOfWork.SaveChangesAsync(ct);
        return count;
    }

    /// <summary>Media của tương tác bị sửa hoặc bị thay không còn dùng; Phase 5 sinh lại (P5b).</summary>
    public async Task RetireMediaAsync(IReadOnlyCollection<int> interactionIds, IReadOnlyCollection<int> optionIds, CancellationToken ct)
    {
        var repo = _unitOfWork.Repository<MediaAsset>();
        List<int> interactionList = [.. interactionIds], optionList = [.. optionIds];
        var assets = await repo.FindAsync(x => x.IsCurrent && !x.IsDeleted &&
            ((x.ListeningInteractionId != null && interactionList.Contains(x.ListeningInteractionId.Value)) ||
             (x.ListeningInteractionOptionId != null && optionList.Contains(x.ListeningInteractionOptionId.Value))), cancellationToken: ct);
        foreach (var asset in assets)
        {
            asset.IsCurrent = false;
            repo.Update(asset);
        }
    }

    /// <summary>Luật trước khi duyệt (spec 5.3, 6.1): khớp kế hoạch, qua rules (kể cả vùng), không có neo lệch.</summary>
    public async Task<ListeningInspection> InspectAsync(ListeningScope scope, CancellationToken ct)
    {
        var saved = await LoadSavedAsync(scope.Version.Id, ct, scope.ParentStoryPackageId);
        var ids = saved.Select(x => x.Id).ToList();
        var options = (await _unitOfWork.Repository<ListeningInteractionOption>().FindAsync(
                x => ids.Contains(x.ListeningInteractionId) && !x.IsDeleted, cancellationToken: ct))
            .ToLookup(x => x.ListeningInteractionId);
        var content = scope.Version.Content!;
        var items = new List<ListeningSavedItem>();
        var issues = new List<ListeningIssue>();
        foreach (var type in saved.Select(x => x.Type).Distinct().Where(t => scope.Plan.Items.All(p => p.Type != t)))
            issues.Add(new("LISTENING_NOT_IN_PLAN", type));
        foreach (var planItem in scope.Plan.Items)
        {
            // Thứ tự lưu = thứ tự AI trả = thứ tự vùng, nên sắp theo Id.
            var rows = saved.Where(x => x.Type == planItem.Type).OrderBy(x => x.Id).ToArray();
            if (rows.Length == 0)
            {
                issues.Add(new("LISTENING_MISSING", planItem.Type));
                continue;
            }
            var rowOptions = rows.Select(x => (IReadOnlyList<ListeningInteractionOption>)options[x.Id].OrderBy(o => o.SortOrder).ToArray()).ToArray();
            var drafts = rows.Select((x, i) => ListeningInteractionMapper.ToDraft(x, rowOptions[i], scope.Blocks)).ToArray();
            var stale = drafts.SelectMany((d, i) => ListeningInteractionRules.CheckStale(d, i, content, scope.Blocks)).ToArray();
            items.AddRange(rows.Select((x, i) => new ListeningSavedItem(x, rowOptions[i], drafts[i], stale.Any(e => e.ItemIndex == i))));
            // Neo lệch thì các luật khác vô nghĩa; người duyệt phải bấm sinh lại loại này.
            var errors = stale.Length > 0
                ? stale
                : ListeningInteractionRules.Validate(drafts, RuleContext(scope, planItem,
                    saved.Where(x => x.Type != planItem.Type).Select(x => x.AnchorOffset).ToArray()));
            issues.AddRange(errors.Select(e => new ListeningIssue(e.Code, planItem.Type, e.ItemIndex, e.Field)));
        }
        return new(items, issues);
    }

    /// <summary>Guardrail chung của Phase 4: không chứa từ thuộc nhóm bị chặn hoặc hạn chế.</summary>
    public static bool IsSafe(string value, AIStoryInputContextSnapshot context) =>
        !PolicyTerms(context.BlockedCategoryTerms, context.BlockedCategoryCodes).Any(term => value.Contains(term.Trim(), StringComparison.OrdinalIgnoreCase)) &&
        !PolicyTerms(context.RestrictedCategoryTerms, context.RestrictedCategoryCodes).Any(term => value.Contains(term.Trim(), StringComparison.OrdinalIgnoreCase));

    public static InvalidOperationException Failure(ListeningInteractionType type, ListeningValidationError error)
    {
        var failure = new InvalidOperationException($"{UpperSnake(type.ToString())}_VALIDATION_FAILED");
        failure.Data["ValidationCode"] = error.Code;
        if (error.ItemIndex.HasValue) failure.Data["LearningItemIndex"] = error.ItemIndex.Value;
        return failure;
    }

    // Snapshot lưu "6-8" / "9-10"; chấp nhận cả tên enum như LearningBatchOptions.
    public static AgeBand ToAgeBand(string? ageBand) => ageBand is "9-10" or "Age_9_10" ? AgeBand.Age_9_10 : AgeBand.Age_6_8;

    private static IReadOnlyList<string> PolicyTerms(IReadOnlyList<string>? terms, IReadOnlyList<string> fallback) =>
        terms is { Count: > 0 } ? terms : fallback;

    private static string UpperSnake(string name) =>
        string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c) ? $"_{c}" : c.ToString())).ToUpperInvariant();
}
