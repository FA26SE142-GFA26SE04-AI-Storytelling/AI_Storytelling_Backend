using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.CreatorAssistance.DTOs;
using StoryPlatform.Application.Features.CreatorAssistance.Interfaces;
using StoryPlatform.Contracts.AI.CreatorAssistance;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.CreatorAssistance.Services;

public sealed partial class CreatorAssistanceService(IUnitOfWork uow, ICreatorAssistanceAiClient ai,
    CreatorAssistanceOptions options, TimeProvider clock, AssistanceCancellationRegistry? cancellations = null,
    ILogger<CreatorAssistanceService>? logger = null) : ICreatorAssistanceService
{
    private readonly AssistanceCancellationRegistry _cancellations = cancellations ?? new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
    private static T Read<T>(string value) => JsonSerializer.Deserialize<T>(value, Json) ?? throw new InvalidOperationException("Invalid assistance data.");
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static AssistanceException Fail(string code, int status = 409) => new(code, status);

    // Every writer uses the same short per-user advisory lock as manual authoring. Read fresh rows after taking it.
    private async Task<T> Locked<T>(int user, int storyId, Func<Story, Task<T>> action, CancellationToken ct)
    {
        if (user <= 0 || storyId <= 0) throw Fail("INVALID_ID", 400);
        await uow.BeginTransactionAsync(ct);
        try
        {
            await uow.AcquireTransactionLockAsync(-user, ct);
            var now = Now;
            if (!await uow.Repository<UserAccount>().ExistsAsync(x => x.Id == user && !x.IsDeleted && x.Status == AccountStatus.Active &&
                (!x.LockedUntil.HasValue || x.LockedUntil <= now), ct)) throw new ForbiddenException();
            var story = await uow.Repository<Story>().FirstOrDefaultAsync(x => x.Id == storyId && !x.IsDeleted &&
                x.Scope == StoryScope.Creator && x.AuthorUserId == user, cancellationToken: ct) ?? throw new NotFoundException("Story", storyId);
            var value = await action(story);
            await uow.CommitTransactionAsync(ct);
            return value;
        }
        catch { await uow.RollbackTransactionAsync(CancellationToken.None); throw; }
    }

    public Task<AssistanceTaskDto> CreateAsync(int userId, int storyId, CreateAssistanceTaskRequest request, CancellationToken ct = default) =>
        Locked(userId, storyId, async story =>
        {
            Key(request.OperationId);
            var hash = Hash(Serialize(request));
            var prior = await uow.Repository<StoryAssistanceTask>().FirstOrDefaultAsync(x => x.StoryId == storyId &&
                x.OwnerUserId == userId && x.CreateOperationId == request.OperationId, cancellationToken: ct);
            if (prior is not null)
            {
                if (prior.CreateRequestHash != hash) throw Fail("OPERATION_PAYLOAD_CONFLICT");
                return Read<AssistanceTaskDto>(prior.CreateResponseJson);
            }
            if (request.RevisionToken != story.AuthoringRevisionToken) throw Fail("STORY_REVISION_CONFLICT");
            var current = await uow.Repository<StoryVersion>().FirstOrDefaultAsync(x => x.StoryId == storyId && x.IsCurrent && !x.IsDeleted, cancellationToken: ct);
            if (request.BaseVersionId != current?.Id) throw Fail("STORY_REVISION_CONFLICT");
            ValidateContext(request);
            var since = Now.AddMinutes(-1);
            if (await uow.Repository<StoryAssistanceTask>().CountAsync(x => x.OwnerUserId == userId && x.CreatedAt >= since, ct) >= options.MaximumRequestsPerMinute)
                throw Fail("ASSISTANCE_RATE_LIMIT", 429);
            var context = await ResolveParentContext(userId, story, request, ct);
            ValidateContext(context);
            var task = new StoryAssistanceTask
            {
                StoryId = storyId, OwnerUserId = userId, EntryPoint = request.EntryPoint,
                SourceVersionId = request.BaseVersionId, SourceRevisionToken = request.RevisionToken,
                AudienceRevision = story.AudienceRevision, Language = story.Language, AgeBand = story.AgeBand,
                ContextJson = Serialize(context), SnapshotHash = Hash(Serialize(new[] { request.EditorSnapshot.Title, request.EditorSnapshot.Content })),
                CreateOperationId = request.OperationId, CreateRequestHash = hash,
                ParentTaskId = request.ParentTaskId, SourceReviewStepId = request.SourceReviewStepId, FindingId = request.FindingId
            };
            await uow.Repository<StoryAssistanceTask>().AddAsync(task, ct);
            await uow.SaveChangesAsync(ct);
            var dto = await Detail(task, story, ct);
            task.CreateResponseJson = Serialize(dto);
            uow.Repository<StoryAssistanceTask>().Update(task);
            return dto;
        }, ct);

    private void ValidateContext(CreateAssistanceTaskRequest request)
    {
        try
        {
            AssistanceValidation.ValidateTarget(request.EntryPoint, request.EditorSnapshot, request.Target);
            AssistanceValidation.ValidateConstraints(request.Constraints);
        }
        catch (ArgumentException) { throw Fail("INVALID_CONTEXT", 400); }
        if (request.EditorSnapshot.Content.Length > options.MaximumSnapshotCharacters || request.Goal is null || request.Goal.Length > 2000)
            throw Fail("CONTEXT_LIMIT_EXCEEDED", 400);
    }

    private async Task<CreateAssistanceTaskRequest> ResolveParentContext(int user, Story story, CreateAssistanceTaskRequest request, CancellationToken ct)
    {
        if (request.ParentTaskId is null && request.SourceReviewStepId is null && request.FindingId is null) return request;
        if (request.ParentTaskId is null || request.SourceReviewStepId is null || string.IsNullOrWhiteSpace(request.FindingId) || request.EntryPoint == "ReviewDraft")
            throw Fail("INVALID_REVIEW_REFERENCE", 400);
        var parent = await TaskRow(user, story.Id, request.ParentTaskId.Value, ct);
        var step = await Step(parent.Id, request.SourceReviewStepId.Value, ct);
        var result = step.OutputJson is null ? null : Read<AssistanceAiResult>(step.OutputJson);
        var finding = result?.Findings.SingleOrDefault(f => f.Id == request.FindingId);
        if (step.Status != "Succeeded" || result?.ResultType != "ReviewFindings" || finding is null) throw Fail("INVALID_REVIEW_REFERENCE", 400);
        var context = Read<CreateAssistanceTaskRequest>(parent.ContextJson);
        if (context.EditorSnapshot != request.EditorSnapshot || parent.AudienceRevision != story.AudienceRevision) throw Fail("EDITOR_CONTEXT_STALE");
        if (request.Target.Start < finding.Start || request.Target.Start + request.Target.Length > finding.Start + finding.Length)
            throw Fail("INVALID_REVIEW_TARGET", 400);
        return request with
        {
            Goal = string.IsNullOrWhiteSpace(request.Goal) ? finding.SuggestedGoal : request.Goal,
            Constraints = new(context.Constraints.Keep.Concat(request.Constraints.Keep).Distinct().ToArray(),
                context.Constraints.Avoid.Concat(request.Constraints.Avoid).Distinct().ToArray())
        };
    }

    public Task<AssistanceTaskDto> GetAsync(int userId, int storyId, int taskId, CancellationToken ct = default) => Locked(userId, storyId, async story =>
    {
        var task = await TaskRow(userId, storyId, taskId, ct);
        await Recover(task, ct);
        return await Detail(task, story, ct);
    }, ct);

    public Task<PagedResult<AssistanceTaskSummary>> ListAsync(int userId, int storyId, int pageIndex, int pageSize, CancellationToken ct = default) =>
        Locked(userId, storyId, async _ =>
        {
            Page(pageIndex, pageSize);
            var rows = await uow.Repository<StoryAssistanceTask>().GetPagedAsync(pageIndex, pageSize,
                x => x.StoryId == storyId && x.OwnerUserId == userId && !x.IsDeleted, q => q.OrderByDescending(x => x.Id), cancellationToken: ct);
            return new PagedResult<AssistanceTaskSummary>(rows.Items.Select(x => new AssistanceTaskSummary(x.Id, x.EntryPoint, x.Status, x.TaskRevision, x.CreatedAt)).ToList(), rows.TotalCount, pageIndex, pageSize);
        }, ct);

    public Task<PagedResult<AssistanceStepDto>> StepsAsync(int userId, int storyId, int taskId, int pageIndex, int pageSize, CancellationToken ct = default) =>
        Locked(userId, storyId, async _ =>
        {
            Page(pageIndex, pageSize);
            var task = await TaskRow(userId, storyId, taskId, ct);
            await Recover(task, ct);
            var rows = await uow.Repository<StoryAssistanceStep>().GetPagedAsync(pageIndex, pageSize,
                x => x.TaskId == taskId && !x.IsDeleted, q => q.OrderByDescending(x => x.Sequence), cancellationToken: ct);
            return new PagedResult<AssistanceStepDto>(rows.Items.Select(ToStep).ToList(), rows.TotalCount, pageIndex, pageSize);
        }, ct);

    private static void Key(string key) { if (string.IsNullOrWhiteSpace(key) || key.Length > 100) throw Fail("INVALID_OPERATION_ID", 400); }
    private static void Page(int page, int size) { if (page < 1 || size is < 1 or > 100) throw Fail("INVALID_PAGE", 400); }
    private async Task<StoryAssistanceTask> TaskRow(int user, int story, int taskId, CancellationToken ct) =>
        await uow.Repository<StoryAssistanceTask>().FirstOrDefaultAsync(x => x.Id == taskId && x.StoryId == story && x.OwnerUserId == user && !x.IsDeleted,
            cancellationToken: ct) ?? throw new NotFoundException("AssistanceTask", taskId);
    private async Task<StoryAssistanceStep> Step(int taskId, int stepId, CancellationToken ct) =>
        await uow.Repository<StoryAssistanceStep>().FirstOrDefaultAsync(x => x.Id == stepId && x.TaskId == taskId && !x.IsDeleted, cancellationToken: ct)
        ?? throw new NotFoundException("AssistanceStep", stepId);

    private async Task Recover(StoryAssistanceTask task, CancellationToken ct)
    {
        if (task.ActiveOperationId is null || task.LeaseExpiresAt > Now) return;
        var step = await uow.Repository<StoryAssistanceStep>().FirstOrDefaultAsync(x => x.TaskId == task.Id && x.OperationId == task.ActiveOperationId, cancellationToken: ct);
        if (step is not null && step.Status == "Running")
        {
            task.TaskRevision++;
            step.Status = "Interrupted"; step.ErrorCode = "AI_OPERATION_INTERRUPTED"; step.CompletedAt = Now;
            step.ResultingTaskRevision = task.TaskRevision;
            step.ResponseJson = Serialize(Result(task, step));
            uow.Repository<StoryAssistanceStep>().Update(step);
        }
        task.ActiveOperationId = null; task.LeaseExpiresAt = null;
        uow.Repository<StoryAssistanceTask>().Update(task);
        // Callers re-query receipts/steps with AsNoTracking. Flush tracked recovery changes
        // inside the existing transaction so those reads see Interrupted and the new revision.
        await uow.SaveChangesAsync(ct);
    }

    private static AssistanceStepDto ToStep(StoryAssistanceStep s) => new(s.Id, s.Sequence, s.OperationId, s.Kind, s.Status,
        s.ResultingTaskRevision, s.ErrorCode,
        s.OutputJson is not null && s.Kind is "Suggestions" or "Trial" ? Read<AssistanceAiResult>(s.OutputJson) : null,
        s.OutputJson is not null && s.Kind == "Direction" ? Read<AssistanceDirection>(s.OutputJson) : null, s.CreatedAt, s.DirectionStepId,
        s.OutputJson is not null && s.Kind == "Answer" ? Read<string>(s.OutputJson) : null);

    private async Task<AssistanceTaskDto> Detail(StoryAssistanceTask t, Story story, CancellationToken ct)
    {
        var context = Read<CreateAssistanceTaskRequest>(t.ContextJson);
        var active = t.ActiveOperationId is null ? null : await uow.Repository<StoryAssistanceStep>().FirstOrDefaultAsync(x => x.TaskId == t.Id && x.OperationId == t.ActiveOperationId, cancellationToken: ct);
        var activeResultId = t.ActiveReviewStepId ?? t.ActiveOptionsStepId ?? t.ActiveClarificationStepId;
        var activeResult = activeResultId.HasValue ? (await Step(t.Id, activeResultId.Value, ct)).OutputJson : null;
        return new(t.Id, t.StoryId, t.EntryPoint, t.Status, t.TaskRevision, t.SourceVersionId, t.SourceRevisionToken, t.AudienceRevision,
            t.SnapshotHash, context.EditorSnapshot, context.Target, t.SourceRevisionToken != story.AuthoringRevisionToken,
            t.AudienceRevision != story.AudienceRevision, story.AuthoringRevisionToken, story.AudienceRevision,
            active is null ? null : new(active.OperationId, active.Id, active.Status, t.LeaseExpiresAt),
            t.ActiveClarificationStepId, t.AcceptedAnswerStepId, t.ActiveOptionsStepId, t.ActiveReviewStepId, t.ActiveDirectionStepId, t.ActiveTrialStepId,
            t.ActiveDirectionStepId.HasValue ? ToStep(await Step(t.Id, t.ActiveDirectionStepId.Value, ct)) : null,
            t.ActiveTrialStepId.HasValue ? ToStep(await Step(t.Id, t.ActiveTrialStepId.Value, ct)) : null,
            t.ParentTaskId, t.SourceReviewStepId, t.FindingId, activeResult is null ? null : Read<AssistanceAiResult>(activeResult),
            t.AcceptedAnswerStepId.HasValue ? Read<string>((await Step(t.Id, t.AcceptedAnswerStepId.Value, ct)).OutputJson!) : null,
            context.Goal, context.Constraints, t.Language, t.AgeBand);
    }

    private static AssistanceCommandResult Result(StoryAssistanceTask task, StoryAssistanceStep step) => new(task.Id, step.Id,
        step.ResultingTaskRevision, task.Status, step.Status == "Running", ToStep(step),
        step.Status == "Running" ? $"/api/v1/creator/stories/{task.StoryId}/assistance-tasks/{task.Id}" : null, task.SnapshotHash);
}
