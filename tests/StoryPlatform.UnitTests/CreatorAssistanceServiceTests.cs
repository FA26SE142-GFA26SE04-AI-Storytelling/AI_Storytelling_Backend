using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Moq;
using Microsoft.EntityFrameworkCore;
using Xunit;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.CreatorAssistance;
using StoryPlatform.Application.Features.CreatorAssistance.DTOs;
using StoryPlatform.Application.Features.CreatorAssistance.Interfaces;
using StoryPlatform.Application.Features.CreatorAssistance.Services;
using StoryPlatform.Application.Features.CreatorAuthoring;
using StoryPlatform.Application.Features.CreatorAuthoring.DTOs;
using StoryPlatform.Application.Features.CreatorAuthoring.Services;
using StoryPlatform.Contracts.AI.CreatorAssistance;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.Security;
using StoryPlatform.Infrastructure.Persistence;

namespace StoryPlatform.UnitTests;

public sealed class CreatorAssistanceServiceTests
{
    [Theory]
    [InlineData(60, 74, false)]
    [InlineData(60, 75, true)]
    [InlineData(60, 90, true)]
    [InlineData(150, 151, false)]
    [InlineData(150, 165, true)]
    [InlineData(151, 180, false)]
    public void LeaseMustIncludeCompletionBudgetAndSafetyMargin(int timeout, int lease, bool valid) =>
        Assert.Equal(valid, new CreatorAssistanceOptions { TimeoutSeconds = timeout, LeaseSeconds = lease }.HasValidLimits());

    [Theory]
    [InlineData("provider-cancel", "AI_SERVICE_UNAVAILABLE", 502)]
    [InlineData("provider-timeout", "AI_TIMEOUT", 504)]
    [InlineData("upstream-timeout", "AI_TIMEOUT", 504)]
    public async Task ProviderCancellationIsNotMisclassifiedAsCoreDeadline(string failure, string code, int status)
    {
        var s = new AssistanceStore(); var task = await s.Create();
        s.Generate = _ => throw (failure switch
        {
            "provider-cancel" => new TaskCanceledException(),
            "provider-timeout" => new TimeoutException(),
            _ => new AssistanceException("AI_TIMEOUT", 504)
        });
        var command = s.Suggest(task.TaskRevision);
        var error = await Assert.ThrowsAsync<AssistanceException>(() => s.Service.SuggestAsync(1, 1, task.TaskId, command));
        Assert.Equal(code, error.Code); Assert.Equal(status, error.StatusCode);
        var replay = await Assert.ThrowsAsync<AssistanceException>(() => s.Service.SuggestAsync(1, 1, task.TaskId, command));
        Assert.Equal(code, replay.Code); Assert.Equal(status, replay.StatusCode); Assert.Equal(1, s.Calls);
        Assert.Null((await s.Service.GetAsync(1, 1, task.TaskId)).ActiveOperation);
        Assert.Empty(s.Items<StoryVersion>());
        s.Generate = _ => Task.FromResult(new AssistanceAiResult { ResultType = "Options", Options = [new("a", "One", "First"), new("b", "Two", "Second")] });
        var latest = await s.Service.GetAsync(1, 1, task.TaskId);
        var retry = await s.Service.SuggestAsync(1, 1, task.TaskId, new() { OperationId = "retry-provider", ExpectedTaskRevision = latest.TaskRevision });
        Assert.Equal("OptionsReady", retry.Status); Assert.Equal(2, s.Calls);
    }
    [Fact]
    public async Task RateLimitQueryTranslatesWithActualNpgsqlProvider()
    {
        var s = new AssistanceStore(); var task = await s.Create();
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=translation_only;Username=unused;Password=unused").Options);
        var translated = false;
        Mock.Get(s.Uow.Object.Repository<StoryAssistanceStep>())
            .Setup(x => x.CountAsync(It.IsAny<Expression<Func<StoryAssistanceStep, bool>>>(), It.IsAny<CancellationToken>()))
            .Returns<Expression<Func<StoryAssistanceStep, bool>>, CancellationToken>((predicate, _) =>
            {
                // Query translation only: captures the production expression without opening a connection.
                var sql = db.Set<StoryAssistanceStep>().Where(predicate).ToQueryString();
                Assert.Contains("story_assistance_steps", sql);
                translated = true;
                return Task.FromResult(s.Items<StoryAssistanceStep>().Count(predicate.Compile()));
            });
        var result = await s.Service.SuggestAsync(1, 1, task.TaskId, s.Suggest(task.TaskRevision));
        Assert.True(translated);
        Assert.Equal("OptionsReady", result.Status);
    }
    [Fact]
    public async Task TimeoutIsPersistedAndReplayedWithoutRetryingProvider()
    {
        var s = new AssistanceStore(new CreatorAssistanceOptions { TimeoutSeconds = 1 }); var task = await s.Create();
        var lateProvider = new TaskCompletionSource<AssistanceAiResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        s.Generate = _ => lateProvider.Task;
        var request = s.Suggest(task.TaskRevision);
        var error = await Assert.ThrowsAsync<AssistanceException>(() => s.Service.SuggestAsync(1, 1, task.TaskId, request));
        Assert.Equal("AI_TIMEOUT", error.Code); Assert.Equal(504, error.StatusCode);
        var replay = await Assert.ThrowsAsync<AssistanceException>(() => s.Service.SuggestAsync(1, 1, task.TaskId, request));
        Assert.Equal(504, replay.StatusCode); Assert.Equal(1, s.Calls);
        Assert.Null((await s.Service.GetAsync(1, 1, task.TaskId)).ActiveOperation);
        lateProvider.SetResult(new AssistanceAiResult { ResultType = "Options", Options = [new("a", "One", "First"), new("b", "Two", "Second")] });
        await Task.Yield();
        var detail = await s.Service.GetAsync(1, 1, task.TaskId);
        Assert.Equal("Open", detail.Status); Assert.Null(detail.ActiveOptionsStepId);
        Assert.Contains(s.Items<StoryAssistanceStep>(), x => x.Kind == "Suggestions" && x.Status == "Failed" && x.ErrorCode == "AI_TIMEOUT");
        Assert.Empty(s.Items<StoryVersion>());
    }
    [Fact]
    public async Task ClosedTaskIsReadOnly_ClosingAgainDoesNotAppendSteps()
    {
        var s = new AssistanceStore(); var task = await s.Create();
        var closed = await s.Service.CloseAsync(1, 1, task.TaskId, new() { OperationId = "close", ExpectedTaskRevision = task.TaskRevision });
        var count = s.Items<StoryAssistanceStep>().Count;
        await s.Service.CloseAsync(1, 1, task.TaskId, new() { OperationId = "close-again", ExpectedTaskRevision = closed.TaskRevision });
        Assert.Equal(count, s.Items<StoryAssistanceStep>().Count);
        await Assert.ThrowsAsync<AssistanceException>(() => s.Service.SuggestAsync(1, 1, task.TaskId, s.Suggest(closed.TaskRevision)));
    }

    [Fact]
    public async Task ClosedTaskSuccessfulTrialCanStillBeReferenced()
    {
        var s = new AssistanceStore(); var (task, _, trial) = await s.Trial();
        await s.Service.CloseAsync(1, 1, task.TaskId, new() { OperationId = "close", ExpectedTaskRevision = trial.TaskRevision });
        var saved = await s.Authoring.SaveContentAsync(1, 1, new() { OperationId = "save", RevisionToken = "revision", Content = "User text",
            AssistanceReferences = [new(task.TaskId, trial.StepId, "TrialUsed")] });
        Assert.Single(saved.Version.Assistance!.NewReferences);
    }

    [Fact]
    public async Task ProviderFailureReplayDoesNotRunInferenceOrExposeProviderMessage()
    {
        var s = new AssistanceStore(); var task = await s.Create();
        s.Generate = _ => throw new HttpRequestException("secret provider content");
        var request = s.Suggest(task.TaskRevision);
        var first = await Assert.ThrowsAsync<AssistanceException>(() => s.Service.SuggestAsync(1, 1, task.TaskId, request));
        var replay = await Assert.ThrowsAsync<AssistanceException>(() => s.Service.SuggestAsync(1, 1, task.TaskId, request));
        Assert.Equal(first.Code, replay.Code); Assert.DoesNotContain("secret", first.Message); Assert.Equal(1, s.Calls);
    }

    [Fact]
    public async Task InvalidAiOutputFailsWithoutChangingStoryOrReadyState()
    {
        var s = new AssistanceStore(); var task = await s.Create();
        s.Generate = _ => Task.FromResult(new AssistanceAiResult { ResultType = "Options", Options = [new("a", "only one", "invalid")] });
        var error = await Assert.ThrowsAsync<AssistanceException>(() => s.Service.SuggestAsync(1, 1, task.TaskId, s.Suggest(task.TaskRevision)));
        Assert.Equal("AI_OUTPUT_INVALID", error.Code);
        Assert.Equal("Open", (await s.Service.GetAsync(1, 1, task.TaskId)).Status);
        Assert.Empty(s.Items<StoryVersion>());
    }

    [Fact]
    public async Task AccountLockIsEnforcedAndTechnicalRateLimitIsNotPackageGate()
    {
        var s = new AssistanceStore(); s.Items<UserAccount>()[0].LockedUntil = DateTime.UtcNow.AddDays(1);
        await Assert.ThrowsAsync<ForbiddenException>(() => s.Create());
        s.Items<UserAccount>()[0].LockedUntil = null;
        for (var i = 0; i < 10; i++) await s.Create(s.Request with { OperationId = "create-" + i });
        var error = await Assert.ThrowsAsync<AssistanceException>(() => s.Create(s.Request with { OperationId = "excess" }));
        Assert.Equal(429, error.StatusCode);
    }

    [Fact]
    public async Task TwoCompetingDirectionCommandsCannotOverwriteEachOther()
    {
        var s = new AssistanceStore(); var task = await s.Create();
        var opts = await s.Service.SuggestAsync(1, 1, task.TaskId, s.Suggest(task.TaskRevision));
        async Task<bool> Confirm(string id)
        {
            try { await s.Service.DirectionAsync(1, 1, task.TaskId, new() { OperationId = id, ExpectedTaskRevision = opts.TaskRevision, OptionsStepId = opts.StepId, DirectionText = id }); return true; }
            catch (AssistanceException e) when (e.Code == "TASK_REVISION_CONFLICT") { return false; }
        }
        Assert.Single(await Task.WhenAll(Confirm("first"), Confirm("second")), x => x);
        Assert.Single(s.Items<StoryAssistanceStep>(), x => x.Kind == "Direction");
    }

    [Fact]
    public async Task CallerCancellationKeepsTaskAndExistingVersions()
    {
        var s = new AssistanceStore(); var task = await s.Create();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        s.Generate = _ => { entered.SetResult(); return new TaskCompletionSource<AssistanceAiResult>().Task; };
        using var cancellation = new CancellationTokenSource();
        var running = s.Service.SuggestAsync(1, 1, task.TaskId, s.Suggest(task.TaskRevision), cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel();
        var result = await running.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Cancelled", result.Step.Status);
        Assert.Empty(s.Items<StoryVersion>());
    }

    [Fact]
    public async Task SnapshotUsesUnsavedText_AndNeverCreatesStoryVersions()
    {
        var s = new AssistanceStore(); var task = await s.Create();
        var result = await s.Service.SuggestAsync(1, 1, task.TaskId, s.Suggest(task.TaskRevision));
        Assert.Equal("OptionsReady", result.Status);
        Assert.Equal("Na muốn sửa xe.", s.LastInput!.Snapshot.Content);
        Assert.Equal("DB original", s.Items<Story>().Single().Content);
        Assert.Empty(s.Items<StoryVersion>());
    }

    [Fact]
    public async Task CreateAndSuggestionReplay_KeepReceipt_AndRejectDifferentPayload()
    {
        var s = new AssistanceStore(); var task = await s.Create();
        Assert.Equal(JsonSerializer.Serialize(task), JsonSerializer.Serialize(await s.Create()));
        var request = s.Suggest(task.TaskRevision);
        var first = await s.Service.SuggestAsync(1, 1, task.TaskId, request);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(await s.Service.SuggestAsync(1, 1, task.TaskId, request)));
        Assert.Equal(1, s.Calls);
        var ex = await Assert.ThrowsAsync<AssistanceException>(() => s.Service.SuggestAsync(1, 1, task.TaskId, request with { AnswerStepId = 123 }));
        Assert.Equal("OPERATION_PAYLOAD_CONFLICT", ex.Code);
    }

    [Fact]
    public async Task DirectionTrialAndSave_KeepOriginsAndLineageWithoutInventingNewAiUsage()
    {
        var s = new AssistanceStore();
        var (task, direction, trial) = await s.Trial();
        Assert.Equal("TrialReady", trial.Status);
        Assert.Equal("insert", trial.Step.Result!.Patch!.Kind);
        Assert.Equal(15, trial.Step.Result.Patch.Start);
        Assert.Empty(s.Items<StoryVersion>());
        var save = new SaveCreatorContentRequest { OperationId = "save1", RevisionToken = "revision", Title = "Story",
            Content = "Creator edited trial", AssistanceReferences = [new(task.TaskId, trial.StepId, "TrialUsed")] };
        var first = await s.Authoring.SaveContentAsync(1, 1, save);
        Assert.Equal("Write", first.Version.Origin);
        Assert.Single(first.Version.Assistance!.NewReferences);
        await s.Authoring.SaveContentAsync(1, 1, save);
        Assert.Single(s.Items<StoryVersion>());
        var second = await s.Authoring.SaveContentAsync(1, 1, save with { OperationId = "save2", RevisionToken = first.Story.RevisionToken,
            BaseVersionId = first.Version.VersionId, AssistanceReferences = null, Content = "Manual edit" });
        Assert.Empty(second.Version.Assistance!.NewReferences);
        Assert.Equal(first.Version.VersionId, second.Version.BaseVersionId);
        var restored = await s.Authoring.RestoreVersionAsync(1, 1, new RestoreCreatorVersionRequest { OperationId = "restore",
            RevisionToken = second.Story.RevisionToken, BaseVersionId = second.Version.VersionId, SourceVersionId = first.Version.VersionId });
        Assert.Empty(restored.Version.Assistance!.NewReferences);
        Assert.Equal(first.Version.VersionId, restored.Version.RestoredFromVersionId);
        Assert.NotNull(restored.Version.Assistance.RestoredSourceSummary);
        Assert.Equal(second.Version.VersionId, restored.Version.BaseVersionId);
        Assert.Equal("Creator edited trial", restored.Version.Content);
    }

    [Fact]
    public async Task ChangeDirectionInvalidatesActiveTrial_ButPreservesHistory()
    {
        var s = new AssistanceStore(); var (task, _, trial) = await s.Trial();
        var detail = await s.Service.GetAsync(1, 1, task.TaskId);
        var direction = await s.Service.DirectionAsync(1, 1, task.TaskId, new()
        { OperationId = "direction2", ExpectedTaskRevision = detail.TaskRevision, OptionsStepId = detail.ActiveOptionsStepId!.Value, DirectionText = "A new direction" });
        detail = await s.Service.GetAsync(1, 1, task.TaskId);
        Assert.Null(detail.ActiveTrialStepId);
        Assert.Equal("DirectionConfirmed", detail.Status);
        Assert.Contains((await s.Service.StepsAsync(1, 1, task.TaskId, 1, 20)).Items, x => x.StepId == trial.StepId && x.Status == "Succeeded");
        await Assert.ThrowsAsync<AssistanceException>(() => s.Service.TrialAsync(1, 1, task.TaskId, new() { OperationId = "wrong-direction", ExpectedTaskRevision = detail.TaskRevision, DirectionStepId = trial.Step.DirectionStepId!.Value }));
    }

    [Fact]
    public async Task AnswerSurvivesProviderFailure_AndRetryDoesNotAskAgain()
    {
        var s = new AssistanceStore(); var task = await s.Create();
        s.Generate = _ => Task.FromResult(new AssistanceAiResult { ResultType = "Clarification", Question = new("q", "Which direction?", [new("a", "Friendship"), new("b", "Persistence")]) });
        var question = await s.Service.SuggestAsync(1, 1, task.TaskId, s.Suggest(task.TaskRevision));
        var answer = await s.Service.AnswerAsync(1, 1, task.TaskId, new() { OperationId = "answer", ExpectedTaskRevision = question.TaskRevision,
            ClarificationStepId = question.StepId, QuestionId = "q", ChoiceId = "a" });
        Assert.Equal(1, s.Calls); // Answer is committed without inference.
        s.Generate = _ => throw new HttpRequestException("sensitive upstream response");
        await Assert.ThrowsAsync<AssistanceException>(() => s.Service.SuggestAsync(1, 1, task.TaskId, new() { OperationId = "after-answer", ExpectedTaskRevision = answer.TaskRevision, AnswerStepId = answer.StepId }));
        var detail = await s.Service.GetAsync(1, 1, task.TaskId);
        Assert.Equal(answer.StepId, detail.AcceptedAnswerStepId);
        s.Generate = _ => Task.FromResult(AssistanceStore.Options());
        var result = await s.Service.SuggestAsync(1, 1, task.TaskId, new() { OperationId = "retry", ExpectedTaskRevision = detail.TaskRevision, AnswerStepId = answer.StepId });
        Assert.Equal("OptionsReady", result.Status);
        Assert.Equal("Friendship", s.LastInput!.Answer);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelOrCloseDuringRunning_PreventsLateResultCommit(bool close)
    {
        var s = new AssistanceStore(); var task = await s.Create();
        var pending = new TaskCompletionSource<AssistanceAiResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        s.Generate = _ => { entered.SetResult(); return pending.Task; };
        var request = s.Suggest(task.TaskRevision);
        var running = s.Service.SuggestAsync(1, 1, task.TaskId, request);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var duplicate = await s.Service.SuggestAsync(1, 1, task.TaskId, request);
        Assert.True(duplicate.IsRunning); Assert.Equal(1, s.Calls);
        var detail = await s.Service.GetAsync(1, 1, task.TaskId);
        Assert.NotNull(detail.ActiveOperation);
        await Assert.ThrowsAsync<AssistanceException>(() => s.Service.CancelAsync(1, 1, task.TaskId, new() { OperationId = "wrong-target", ExpectedTaskRevision = detail.TaskRevision, TargetOperationId = "different" }));
        if (close) await s.Service.CloseAsync(1, 1, task.TaskId, new() { OperationId = "close", ExpectedTaskRevision = detail.TaskRevision });
        else await s.Service.CancelAsync(1, 1, task.TaskId, new() { OperationId = "cancel-command", ExpectedTaskRevision = detail.TaskRevision, TargetOperationId = request.OperationId });
        pending.SetResult(AssistanceStore.Options());
        var result = await running;
        Assert.Equal("Cancelled", result.Step.Status);
        detail = await s.Service.GetAsync(1, 1, task.TaskId);
        Assert.Null(detail.ActiveOptionsStepId); Assert.Null(detail.ActiveOperation);
        Assert.Equal(close ? "Closed" : "Open", detail.Status);
    }

    [Fact]
    public async Task EmptyReviewIsReady_WholeDraftCannotGenerateTrial()
    {
        var s = new AssistanceStore(); var task = await s.Create(s.Request with { EntryPoint = "ReviewDraft", Target = new("wholeDraft", 0, 15) });
        s.Generate = _ => Task.FromResult(new AssistanceAiResult { ResultType = "ReviewFindings", ChecksPerformed = ["continuity"] });
        var result = await s.Service.SuggestAsync(1, 1, task.TaskId, s.Suggest(task.TaskRevision));
        Assert.Equal("ReviewReady", result.Status); Assert.Empty(result.Step.Result!.Findings);
        Assert.Equal("FullSnapshot", result.Step.Result.Coverage);
        await Assert.ThrowsAsync<AssistanceException>(() => s.Service.TrialAsync(1, 1, task.TaskId, new() { OperationId = "trial", ExpectedTaskRevision = result.TaskRevision, DirectionStepId = result.StepId }));
    }

    [Fact]
    public async Task ReviewChildMustUseValidFindingAndUnchangedSnapshot()
    {
        var s = new AssistanceStore(); var task = await s.Create(s.Request with { EntryPoint = "ReviewDraft", Target = new("wholeDraft", 0, 15), Constraints = new(["Keep respect"], []) });
        s.Generate = _ => Task.FromResult(new AssistanceAiResult { ResultType = "ReviewFindings", ChecksPerformed = ["motive"],
            Findings = [new("f1", 0, 2, "Na", "Clarify motivation", "Character motivation")] });
        var review = await s.Service.SuggestAsync(1, 1, task.TaskId, s.Suggest(task.TaskRevision));
        var child = s.Request with { OperationId = "child", EntryPoint = "SupportSelection", Target = new("selection", 0, 2),
            ParentTaskId = task.TaskId, SourceReviewStepId = review.StepId, FindingId = "f1" };
        var created = await s.Create(child);
        Assert.Equal(task.TaskId, created.ParentTaskId);
        Assert.Contains("Keep respect", created.Constraints!.Keep);
        Assert.Equal("Character motivation", created.Goal);
        await Assert.ThrowsAsync<AssistanceException>(() => s.Create(child with { OperationId = "changed-child", EditorSnapshot = new("Story", "Na muốn sửa xe!") }));
    }

    [Fact]
    public async Task LeaseRecoveryInterruptsStep_AndLateResponseIsDiscarded()
    {
        var s = new AssistanceStore(); var task = await s.Create();
        var pending = new TaskCompletionSource<AssistanceAiResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        s.Generate = _ => { entered.SetResult(); return pending.Task; };
        var running = s.Service.SuggestAsync(1, 1, task.TaskId, s.Suggest(task.TaskRevision));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        s.Clock.Value = s.Clock.Value.AddMinutes(3);
        var detail = await s.Service.GetAsync(1, 1, task.TaskId);
        Assert.Null(detail.ActiveOperation);
        pending.SetResult(AssistanceStore.Options());
        Assert.Equal("Interrupted", (await running).Step.Status);
    }

    [Theory]
    [InlineData("Retry")]
    [InlineData("Steps")]
    [InlineData("Completion")]
    public async Task LeaseRecovery_FirstRequestReadsFlushedInterruptedStep(string firstReader)
    {
        var s = new AssistanceStore(); var task = await s.Create();
        var pending = new TaskCompletionSource<AssistanceAiResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        s.Generate = _ => { entered.SetResult(); return pending.Task; };
        var request = s.Suggest(task.TaskRevision);
        var running = s.Service.SuggestAsync(1, 1, task.TaskId, request);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var runningRevision = s.Items<StoryAssistanceTask>().Single().TaskRevision;
        s.Clock.Value = s.Clock.Value.AddMinutes(3);
        try
        {
            if (firstReader == "Retry")
            {
                var replay = await s.Service.SuggestAsync(1, 1, task.TaskId, request);
                Assert.False(replay.IsRunning);
                Assert.Null(replay.PollUrl);
                Assert.Equal("Interrupted", replay.Step.Status);
                Assert.Equal("AI_OPERATION_INTERRUPTED", replay.Step.ErrorCode);
                Assert.True(replay.TaskRevision > runningRevision);
            }
            else if (firstReader == "Steps")
            {
                var step = Assert.Single((await s.Service.StepsAsync(1, 1, task.TaskId, 1, 20)).Items);
                Assert.Equal("Interrupted", step.Status);
                Assert.Equal("AI_OPERATION_INTERRUPTED", step.ErrorCode);
                Assert.True(step.ResultingTaskRevision > runningRevision);
            }
            pending.SetResult(AssistanceStore.Options());
            var completed = await running.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(completed.IsRunning);
            Assert.Equal("Interrupted", completed.Step.Status);
            Assert.True(completed.TaskRevision > runningRevision);
            Assert.Equal(1, s.Calls);
            var detail = await s.Service.GetAsync(1, 1, task.TaskId);
            Assert.Null(detail.ActiveOperation);
            Assert.Null(detail.ActiveOptionsStepId);
            Assert.Equal(completed.TaskRevision, detail.TaskRevision);
        }
        finally
        {
            pending.TrySetResult(AssistanceStore.Options());
            await running.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StaleStoryOrAudienceBlocksInference(bool audience)
    {
        var s = new AssistanceStore(); var task = await s.Create();
        if (audience) s.Items<Story>()[0].AudienceRevision++; else s.Items<Story>()[0].AuthoringRevisionToken = "changed";
        var error = await Assert.ThrowsAsync<AssistanceException>(() => s.Service.SuggestAsync(1, 1, task.TaskId, s.Suggest(task.TaskRevision)));
        Assert.Equal("EDITOR_CONTEXT_STALE", error.Code); Assert.Equal(0, s.Calls);
    }

    [Fact]
    public async Task WrongOwnerAndParentScopeAreHidden()
    {
        var s = new AssistanceStore(); var task = await s.Create();
        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.GetAsync(2, 1, task.TaskId));
        s.Items<Story>()[0].Scope = StoryScope.Parent;
        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.GetAsync(1, 1, task.TaskId));
    }

    [Fact]
    public async Task StaleTaskRevisionDoesNotChangeDirection()
    {
        var s = new AssistanceStore(); var task = await s.Create();
        var opts = await s.Service.SuggestAsync(1, 1, task.TaskId, s.Suggest(task.TaskRevision));
        await Assert.ThrowsAsync<AssistanceException>(() => s.Service.DirectionAsync(1, 1, task.TaskId, new() { OperationId = "old-revision",
            ExpectedTaskRevision = task.TaskRevision, OptionsStepId = opts.StepId, OptionId = "a" }));
        Assert.Equal("OptionsReady", (await s.Service.GetAsync(1, 1, task.TaskId)).Status);
    }

    [Fact]
    public async Task SaveWithForeignReferenceOrChangedAudienceIsRejectedAtomically()
    {
        var s = new AssistanceStore(); var (task, _, trial) = await s.Trial();
        var request = new SaveCreatorContentRequest { OperationId = "bad-save", RevisionToken = "revision", Content = "No change",
            AssistanceReferences = [new(task.TaskId, trial.StepId, "TrialUsed")] };
        s.Items<StoryAssistanceTask>()[0].OwnerUserId = 2;
        await Assert.ThrowsAsync<AssistanceException>(() => s.Authoring.SaveContentAsync(1, 1, request));
        Assert.Empty(s.Items<StoryVersion>());
        s.Items<StoryAssistanceTask>()[0].OwnerUserId = 1;
        s.Items<Story>()[0].AudienceRevision++;
        await Assert.ThrowsAsync<AssistanceException>(() => s.Authoring.SaveContentAsync(1, 1, request));
        Assert.Empty(s.Items<StoryVersion>());
    }

    [Fact]
    public async Task ContextLimitAndFullBodySelectionRejectedBeforeInference()
    {
        var s = new AssistanceStore();
        await Assert.ThrowsAsync<AssistanceException>(() => s.Create(s.Request with { EntryPoint = "SupportSelection", Target = new("selection", 0, 15) }));
        await Assert.ThrowsAsync<AssistanceException>(() => s.Create(s.Request with { EditorSnapshot = new("Story", new string('a', 30_001)) }));
        Assert.Equal(0, s.Calls); Assert.Empty(s.Items<StoryAssistanceTask>());
    }
}

internal sealed class AssistanceStore
{
    private readonly Dictionary<Type, object> _data = [];
    private Dictionary<Type, string>? _backup;
    private readonly Dictionary<(Type Type, int Id), Action> _pendingUpdates = [];
    private readonly SemaphoreSlim _transaction = new(1, 1);
    public sealed class TestClock : TimeProvider { public DateTimeOffset Value = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Value; }
    public TestClock Clock { get; } = new();
    public Mock<IUnitOfWork> Uow { get; } = new();
    public CreatorAssistanceService Service { get; }
    public CreatorAuthoringService Authoring { get; }
    public Func<AssistanceAiRequest, Task<AssistanceAiResult>> Generate { get; set; }
    public AssistanceAiRequest? LastInput { get; private set; }
    public int Calls { get; private set; }
    public CreateAssistanceTaskRequest Request => new() { OperationId = "create", RevisionToken = "revision", EditorSnapshot = new("Story", "Na muốn sửa xe."), Target = new("cursor", 15, 0) };
    public SuggestAssistanceRequest Suggest(int revision) => new() { OperationId = "suggest", ExpectedTaskRevision = revision };
    public Task<AssistanceTaskDto> Create(CreateAssistanceTaskRequest? request = null) => Service.CreateAsync(1, 1, request ?? Request);
    public static AssistanceAiResult Options() => new() { ResultType = "Options", Options = [new("a", "Cooperation", "Friends work together"), new("b", "Persistence", "Try again")], Provider = "fake", Model = "test" };
    public AssistanceStore(CreatorAssistanceOptions? limits = null)
    {
        Register<UserAccount>(); Register<Story>(); Register<StoryVersion>(); Register<AuditLog>(); Register<ModerationItem>(); Register<StoryAssistanceTask>(); Register<StoryAssistanceStep>();
        Uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns<CancellationToken>(async ct =>
        { await _transaction.WaitAsync(ct); _backup = _data.ToDictionary(x => x.Key, x => JsonSerializer.Serialize(x.Value, x.Value.GetType())); });
        Uow.Setup(x => x.AcquireTransactionLockAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        Uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(() => { FlushUpdates(); return Task.FromResult(1); });
        Uow.Setup(x => x.CommitTransactionAsync(It.IsAny<CancellationToken>())).Returns(() => { FlushUpdates(); _backup = null; _transaction.Release(); return Task.CompletedTask; });
        Uow.Setup(x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>())).Returns(() =>
        {
            foreach (var (type, json) in _backup!) _data[type] = JsonSerializer.Deserialize(json, typeof(List<>).MakeGenericType(type))!;
            _pendingUpdates.Clear();
            _backup = null; _transaction.Release(); return Task.CompletedTask;
        });
        Items<UserAccount>().AddRange([new() { Id = 1, Status = AccountStatus.Active }, new() { Id = 2, Status = AccountStatus.Active }]);
        Items<Story>().Add(new() { Id = 1, AuthorUserId = 1, Scope = StoryScope.Creator, Source = StorySource.Manual, Content = "DB original", AuthoringRevisionToken = "revision", Language = "vi", AgeBand = "6-8" });
        Generate = request => Task.FromResult(request.Mode == "Trial" ? new AssistanceAiResult { ResultType = "Trial", Patch = new("malicious-position", 10000, 50, "Hai bạn cùng sửa xe.", "wrong quote"), Provider = "fake" } : Options());
        var ai = new Mock<ICreatorAssistanceAiClient>();
        ai.Setup(x => x.GenerateAsync(It.IsAny<AssistanceAiRequest>(), It.IsAny<CancellationToken>())).Returns<AssistanceAiRequest, CancellationToken>((r, _) => { Calls++; LastInput = r; return Generate(r); });
        Service = new(Uow.Object, ai.Object, limits ?? new CreatorAssistanceOptions(), Clock);
        Authoring = new(Uow.Object, new CreatorAuthoringOptions(), new ImportPreviewProtector(new EphemeralDataProtectionProvider()));
    }
    public async Task<(AssistanceTaskDto Task, AssistanceCommandResult Direction, AssistanceCommandResult Trial)> Trial()
    {
        var task = await Create(); var opt = await Service.SuggestAsync(1, 1, task.TaskId, Suggest(task.TaskRevision));
        var direction = await Service.DirectionAsync(1, 1, task.TaskId, new() { OperationId = "direction", ExpectedTaskRevision = opt.TaskRevision, OptionsStepId = opt.StepId, OptionId = "a" });
        var trial = await Service.TrialAsync(1, 1, task.TaskId, new() { OperationId = "trial", ExpectedTaskRevision = direction.TaskRevision, DirectionStepId = direction.StepId });
        return (task, direction, trial);
    }
    public List<T> Items<T>() where T : class => (List<T>)_data[typeof(T)];
    private void FlushUpdates()
    {
        foreach (var update in _pendingUpdates.Values) update();
        _pendingUpdates.Clear();
    }
    private static T Clone<T>(T x) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(x))!;
    private void Register<T>() where T : BaseEntity
    {
        _data[typeof(T)] = new List<T>();
        var repo = new Mock<IGenericRepository<T>>(); Uow.Setup(x => x.Repository<T>()).Returns(repo.Object);
        repo.Setup(x => x.GetByIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>())).Returns<object, CancellationToken>((id, _) => Task.FromResult(Items<T>().FirstOrDefault(x => x.Id == (int)id) is { } found ? Clone(found) : null));
        repo.Setup(x => x.FirstOrDefaultAsync(It.IsAny<Expression<Func<T, bool>>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).Returns<Expression<Func<T, bool>>, string?, CancellationToken>((p, _, _) => Task.FromResult(Items<T>().FirstOrDefault(p.Compile()) is { } found ? Clone(found) : null));
        repo.Setup(x => x.ExistsAsync(It.IsAny<Expression<Func<T, bool>>>(), It.IsAny<CancellationToken>())).Returns<Expression<Func<T, bool>>, CancellationToken>((p, _) => Task.FromResult(Items<T>().Any(p.Compile())));
        repo.Setup(x => x.CountAsync(It.IsAny<Expression<Func<T, bool>>>(), It.IsAny<CancellationToken>())).Returns<Expression<Func<T, bool>>, CancellationToken>((p, _) => Task.FromResult(Items<T>().Count(p.Compile())));
        repo.Setup(x => x.FindAsync(It.IsAny<Expression<Func<T, bool>>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).Returns<Expression<Func<T, bool>>, string?, CancellationToken>((p, _, _) => Task.FromResult<IReadOnlyList<T>>(Items<T>().Where(p.Compile()).Select(Clone).ToList()));
        repo.Setup(x => x.AddAsync(It.IsAny<T>(), It.IsAny<CancellationToken>())).Returns<T, CancellationToken>((e, _) => { e.Id = Items<T>().Select(x => x.Id).DefaultIfEmpty().Max() + 1; e.CreatedAt = Clock.Value.UtcDateTime; Items<T>().Add(Clone(e)); return Task.FromResult(e); });
        // AsNoTracking queries must not see tracked updates until SaveChanges, just like GenericRepository/EF.
        repo.Setup(x => x.Update(It.IsAny<T>())).Callback<T>(e =>
        {
            var snapshot = Clone(e);
            _pendingUpdates[(typeof(T), e.Id)] = () =>
            {
                var rows = Items<T>(); rows[rows.FindIndex(x => x.Id == snapshot.Id)] = snapshot;
            };
        });
        repo.Setup(x => x.GetPagedAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Expression<Func<T, bool>>>(), It.IsAny<Func<IQueryable<T>, IOrderedQueryable<T>>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns<int, int, Expression<Func<T, bool>>?, Func<IQueryable<T>, IOrderedQueryable<T>>?, string?, CancellationToken>((index, size, filter, order, _, _) =>
            { var rows = Items<T>().AsQueryable(); if (filter is not null) rows = rows.Where(filter); var count = rows.Count(); if (order is not null) rows = order(rows); return Task.FromResult<(IReadOnlyList<T>, int)>((rows.Skip((index - 1) * size).Take(size).Select(Clone).ToList(), count)); });
    }
}
