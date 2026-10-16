using StoryPlatform.Application.Features.CreatorAssistance.DTOs;
using StoryPlatform.Contracts.AI.CreatorAssistance;
using StoryPlatform.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace StoryPlatform.Application.Features.CreatorAssistance.Services;

public sealed partial class CreatorAssistanceService
{
    private Task<AssistanceCommandResult> Command(int user, int storyId, int taskId, string kind, AssistanceCommand request,
        Func<Story, StoryAssistanceTask, StoryAssistanceStep, Task> action, bool allowBusy, CancellationToken ct) =>
        Locked(user, storyId, async story =>
        {
            Key(request.OperationId);
            var task = await TaskRow(user, storyId, taskId, ct);
            await Recover(task, ct);
            var hash = Hash(Serialize(new { kind, request = (object)request }));
            var prior = await uow.Repository<StoryAssistanceStep>().FirstOrDefaultAsync(x => x.TaskId == taskId && x.OperationId == request.OperationId, cancellationToken: ct);
            if (prior is not null)
            {
                if (prior.RequestHash != hash) throw Fail("OPERATION_PAYLOAD_CONFLICT");
                if (prior.Status == "Failed") throw Fail(prior.ErrorCode!, prior.ErrorCode == "AI_TIMEOUT" ? 504 : prior.ErrorCode == "EDITOR_CONTEXT_STALE" ? 409 : 502);
                return prior.ResponseJson is null ? Result(task, prior) : Read<AssistanceCommandResult>(prior.ResponseJson);
            }
            if (request.ExpectedTaskRevision != task.TaskRevision) throw Fail("TASK_REVISION_CONFLICT");
            if (task.Status == "Closed" && kind == "Close")
            {
                var closed = await uow.Repository<StoryAssistanceStep>().FirstOrDefaultAsync(x => x.TaskId == taskId && x.Kind == "Close" && x.Status == "Succeeded", cancellationToken: ct);
                return Read<AssistanceCommandResult>(closed!.ResponseJson!);
            }
            if (task.Status == "Closed" && kind != "Close") throw Fail("TASK_CLOSED");
            if (task.ActiveOperationId is not null && !allowBusy) throw Fail("TASK_BUSY");
            var step = new StoryAssistanceStep
            {
                TaskId = taskId, Sequence = ++task.LastSequence, OperationId = request.OperationId, RequestHash = hash,
                Kind = kind, InputJson = Serialize((object)request), Status = "Succeeded"
            };
            await uow.Repository<StoryAssistanceStep>().AddAsync(step, ct);
            await uow.SaveChangesAsync(ct);
            task.TaskRevision++;
            await action(story, task, step);
            step.ResultingTaskRevision = task.TaskRevision;
            if (step.Status != "Running") step.CompletedAt = Now;
            var response = Result(task, step);
            step.ResponseJson = Serialize(response);
            uow.Repository<StoryAssistanceStep>().Update(step);
            uow.Repository<StoryAssistanceTask>().Update(task);
            return response;
        }, ct);

    public Task<AssistanceCommandResult> AnswerAsync(int userId, int storyId, int taskId, AnswerAssistanceRequest request, CancellationToken ct = default) =>
        Command(userId, storyId, taskId, "Answer", request, async (_, task, step) =>
        {
            if (task.Status != "AwaitingClarification" || task.ActiveClarificationStepId != request.ClarificationStepId || task.AcceptedAnswerStepId.HasValue)
                throw Fail("INVALID_TASK_STATE");
            var source = await Step(task.Id, request.ClarificationStepId, ct);
            var question = Read<AssistanceAiResult>(source.OutputJson!).Question!;
            if (question.Id != request.QuestionId) throw Fail("INVALID_QUESTION", 400);
            var selected = string.IsNullOrWhiteSpace(request.ChoiceId) ? null : question.Choices.SingleOrDefault(x => x.Id == request.ChoiceId);
            if (request.ChoiceId is not null && selected is null) throw Fail("INVALID_CHOICE", 400);
            if ((selected is not null ? 1 : 0) + (!string.IsNullOrWhiteSpace(request.CustomAnswer) ? 1 : 0) + (request.LetAiPropose ? 1 : 0) != 1 || request.CustomAnswer?.Length > 2000)
                throw Fail("INVALID_ANSWER", 400);
            step.OutputJson = Serialize(selected?.Text ?? (request.LetAiPropose ? "Let AI propose options and disclose assumptions." : request.CustomAnswer!.Trim()));
            task.AcceptedAnswerStepId = step.Id;
        }, false, ct);

    public Task<AssistanceCommandResult> DirectionAsync(int userId, int storyId, int taskId, ConfirmAssistanceDirectionRequest request, CancellationToken ct = default) =>
        Command(userId, storyId, taskId, "Direction", request, async (_, task, step) =>
        {
            if (task.Status is not ("OptionsReady" or "DirectionConfirmed" or "TrialReady") || task.ActiveOptionsStepId != request.OptionsStepId)
                throw Fail("INVALID_TASK_STATE");
            var source = await Step(task.Id, request.OptionsStepId, ct);
            var selected = Read<AssistanceAiResult>(source.OutputJson!).Options.SingleOrDefault(o => o.Id == request.OptionId);
            if (request.OptionId is not null && selected is null) throw Fail("INVALID_OPTION", 400);
            var text = string.IsNullOrWhiteSpace(request.DirectionText) ? selected?.Direction : request.DirectionText.Trim();
            if (string.IsNullOrWhiteSpace(text) || text.Length > 2000) throw Fail("INVALID_DIRECTION", 400);
            try
            {
                AssistanceValidation.ValidateConstraints(request.ConfirmedConstraints);
                var original = Read<CreateAssistanceTaskRequest>(task.ContextJson).Constraints;
                AssistanceValidation.ValidateConstraints(new(original.Keep.Concat(request.ConfirmedConstraints.Keep).Distinct().ToArray(),
                    original.Avoid.Concat(request.ConfirmedConstraints.Avoid).Distinct().ToArray()));
            }
            catch (ArgumentException) { throw Fail("INVALID_CONSTRAINTS", 400); }
            step.OutputJson = Serialize(new AssistanceDirection(text, request.ConfirmedConstraints));
            step.Provider = source.Provider; step.Model = source.Model; step.PromptVersion = source.PromptVersion;
            task.ActiveDirectionStepId = step.Id; task.ActiveTrialStepId = null; task.Status = "DirectionConfirmed";
        }, false, ct);

    public async Task<AssistanceCommandResult> CancelAsync(int userId, int storyId, int taskId, CancelAssistanceRequest request, CancellationToken ct = default)
    {
        var result = await Command(userId, storyId, taskId, "Cancel", request, async (_, task, _) =>
        {
            if (task.ActiveOperationId != request.TargetOperationId) throw Fail("OPERATION_NOT_RUNNING");
            await CancelActive(task, ct);
        }, true, ct);
        _cancellations.Cancel(taskId, request.TargetOperationId);
        return result;
    }

    public async Task<AssistanceCommandResult> CloseAsync(int userId, int storyId, int taskId, AssistanceCommand request, CancellationToken ct = default)
    {
        string? target = null;
        var result = await Command(userId, storyId, taskId, "Close", request, async (_, task, _) =>
        {
            task.Status = "Closed";
            target = task.ActiveOperationId;
            if (task.ActiveOperationId is not null) await CancelActive(task, ct);
        }, true, ct);
        if (target is not null) _cancellations.Cancel(taskId, target);
        return result;
    }

    private async Task CancelActive(StoryAssistanceTask task, CancellationToken ct)
    {
        var active = await uow.Repository<StoryAssistanceStep>().FirstOrDefaultAsync(x => x.TaskId == task.Id && x.OperationId == task.ActiveOperationId, cancellationToken: ct)
            ?? throw Fail("OPERATION_NOT_RUNNING");
        if (active.Status != "Running") throw Fail("OPERATION_NOT_RUNNING");
        active.Status = "Cancelled"; active.CompletedAt = Now; active.ErrorCode = "AI_OPERATION_CANCELLED";
        active.ResultingTaskRevision = task.TaskRevision;
        task.ActiveOperationId = null; task.LeaseExpiresAt = null;
        active.ResponseJson = Serialize(Result(task, active));
        uow.Repository<StoryAssistanceStep>().Update(active);
    }

    public Task<AssistanceCommandResult> SuggestAsync(int userId, int storyId, int taskId, SuggestAssistanceRequest request, CancellationToken ct = default) =>
        Generate(userId, storyId, taskId, "Suggestions", request, ct);

    public Task<AssistanceCommandResult> TrialAsync(int userId, int storyId, int taskId, CreateAssistanceTrialRequest request, CancellationToken ct = default) =>
        Generate(userId, storyId, taskId, "Trial", request, ct);

    private async Task<AssistanceCommandResult> Generate(int user, int storyId, int taskId, string kind, AssistanceCommand command, CancellationToken ct)
    {
        using var logScope = logger?.BeginScope(new Dictionary<string, object?>
        {
            ["TraceId"] = System.Diagnostics.Activity.Current?.TraceId.ToString(), ["TaskId"] = taskId, ["StoryId"] = storyId
        });
        AssistanceAiRequest? input = null;
        var started = await Command(user, storyId, taskId, kind, command, async (story, task, step) =>
        {
            if (task.SourceRevisionToken != story.AuthoringRevisionToken || task.AudienceRevision != story.AudienceRevision) throw Fail("EDITOR_CONTEXT_STALE");
            var context = Read<CreateAssistanceTaskRequest>(task.ContextJson);
            string? answer = null;
            AssistanceDirection? direction = null;
            if (kind == "Suggestions")
            {
                var request = (SuggestAssistanceRequest)command;
                if (task.Status == "Open")
                {
                    if (request.AnswerStepId.HasValue) throw Fail("INVALID_ANSWER_REFERENCE", 400);
                }
                else if (task.Status == "AwaitingClarification" && task.AcceptedAnswerStepId.HasValue && request.AnswerStepId == task.AcceptedAnswerStepId)
                    answer = Read<string>((await Step(task.Id, task.AcceptedAnswerStepId.Value, ct)).OutputJson!);
                else throw Fail("INVALID_TASK_STATE");
            }
            else
            {
                var request = (CreateAssistanceTrialRequest)command;
                if (task.EntryPoint == "ReviewDraft" || task.Status is not ("DirectionConfirmed" or "TrialReady") || request.DirectionStepId != task.ActiveDirectionStepId)
                    throw Fail("INVALID_TASK_STATE");
                direction = Read<AssistanceDirection>((await Step(task.Id, request.DirectionStepId, ct)).OutputJson!);
                step.DirectionStepId = request.DirectionStepId;
            }
            var since = Now.AddMinutes(-1);
            var taskIds = (await uow.Repository<StoryAssistanceTask>().FindAsync(x => x.OwnerUserId == user, cancellationToken: ct)).Select(x => x.Id).ToArray();
            // Avoid .NET 10's span-based array Contains overload: EF Core 8 cannot evaluate its ReadOnlySpan conversion.
            if (await uow.Repository<StoryAssistanceStep>().CountAsync(x => Enumerable.Contains(taskIds, x.TaskId) && x.CreatedAt >= since &&
                (x.Kind == "Trial" || x.Kind == "Suggestions") && x.Id != step.Id, ct) >= options.MaximumRequestsPerMinute)
                throw Fail("ASSISTANCE_RATE_LIMIT", 429);
            var constraints = new AssistanceConstraints(context.Constraints.Keep.Concat(direction?.Constraints.Keep ?? []).Distinct().ToArray(),
                context.Constraints.Avoid.Concat(direction?.Constraints.Avoid ?? []).Distinct().ToArray());
            input = new AssistanceAiRequest
            {
                Mode = kind, EntryPoint = task.EntryPoint, Snapshot = context.EditorSnapshot, Target = context.Target,
                Language = task.Language, AgeBand = task.AgeBand, Goal = context.Goal, Constraints = constraints,
                Answer = answer, ConfirmedDirection = direction?.ConfirmedDirectionText, MaximumTrialCharacters = options.MaximumTrialCharacters
            };
            step.Status = "Running";
            task.ActiveOperationId = command.OperationId; task.LeaseExpiresAt = Now.AddSeconds(options.LeaseSeconds);
        }, false, ct);
        if (input is null) return started; // Replayed receipt, including an in-flight operation. Never infer twice for a duplicate.

        AssistanceAiResult? output = null;
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        string? error = null;
        var errorStatus = 502;
        var localCancellation = _cancellations.Register(taskId, command.OperationId);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds));
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, localCancellation.Token, deadline.Token);
            output = await ai.GenerateAsync(input, timeout.Token).WaitAsync(timeout.Token);
            output = AssistanceValidation.ValidateResult(input, output);
        }
        catch (OperationCanceledException)
        {
            error = ct.IsCancellationRequested || localCancellation.IsCancellationRequested ? "AI_OPERATION_CANCELLED" :
                deadline.IsCancellationRequested ? "AI_TIMEOUT" : "AI_SERVICE_UNAVAILABLE";
            errorStatus = error == "AI_TIMEOUT" ? 504 : error == "AI_OPERATION_CANCELLED" ? 409 : 502;
        }
        catch (Exception ex)
        {
            error = ex is AssistanceException assistance ? assistance.Code : ex is TimeoutException ? "AI_TIMEOUT" :
                ex is ArgumentException or System.Text.Json.JsonException ? "AI_OUTPUT_INVALID" : "AI_SERVICE_UNAVAILABLE";
            errorStatus = error == "AI_TIMEOUT" ? 504 : 502;
        }
        finally { _cancellations.Release(taskId, command.OperationId); }

        // Complete even if the HTTP caller disconnected. Never write request text into an exception/log.
        using var completion = new CancellationTokenSource(TimeSpan.FromSeconds(CreatorAssistanceOptions.CompletionTimeoutSeconds));
        var finished = await Locked(user, storyId, async story =>
        {
            var task = await TaskRow(user, storyId, taskId, completion.Token);
            await Recover(task, completion.Token);
            var step = await Step(taskId, started.StepId, completion.Token);
            if (step.Status != "Running" || task.ActiveOperationId != command.OperationId || task.Status == "Closed")
                return step.ResponseJson is null ? Result(task, step) : Read<AssistanceCommandResult>(step.ResponseJson);
            task.ActiveOperationId = null; task.LeaseExpiresAt = null; task.TaskRevision++;
            if (story.AudienceRevision != task.AudienceRevision || story.AuthoringRevisionToken != task.SourceRevisionToken)
            { error = "EDITOR_CONTEXT_STALE"; errorStatus = 409; }
            step.Status = error is null ? "Succeeded" : error == "AI_OPERATION_CANCELLED" ? "Cancelled" : "Failed";
            step.ErrorCode = error; step.CompletedAt = Now; step.ResultingTaskRevision = task.TaskRevision;
            if (error is null)
            {
                step.OutputJson = Serialize(output); step.Provider = output!.Provider; step.Model = output.Model; step.PromptVersion = output.PromptVersion;
                switch (output.ResultType)
                {
                    case "Clarification": task.Status = "AwaitingClarification"; task.ActiveClarificationStepId = step.Id; break;
                    case "Options": task.Status = "OptionsReady"; task.ActiveOptionsStepId = step.Id; break;
                    case "ReviewFindings": task.Status = "ReviewReady"; task.ActiveReviewStepId = step.Id; break;
                    case "Trial": task.Status = "TrialReady"; task.ActiveTrialStepId = step.Id; break;
                }
            }
            var result = Result(task, step); step.ResponseJson = Serialize(result);
            uow.Repository<StoryAssistanceStep>().Update(step); uow.Repository<StoryAssistanceTask>().Update(task);
            return result;
        }, completion.Token);
        logger?.LogInformation("Creator assistance completed. TraceId={TraceId}, EntryPoint={EntryPoint}, Mode={Mode}, TaskId={TaskId}, StepId={StepId}, Status={Status}, ErrorCode={ErrorCode}, LatencyMs={LatencyMs}", System.Diagnostics.Activity.Current?.TraceId.ToString(), input.EntryPoint, kind, taskId, finished.StepId, finished.Step.Status, finished.Step.ErrorCode, elapsed.ElapsedMilliseconds);
        if (finished.Step.Status == "Failed") throw Fail(finished.Step.ErrorCode!, errorStatus);
        return finished;
    }
}
