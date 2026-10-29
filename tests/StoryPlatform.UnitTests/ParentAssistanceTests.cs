using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Services;
using StoryPlatform.Application.Features.CreatorAssistance;
using StoryPlatform.Application.Features.CreatorAssistance.DTOs;
using StoryPlatform.Application.Features.CreatorAssistance.Interfaces;
using StoryPlatform.Application.Features.CreatorAssistance.Services;
using StoryPlatform.Application.Features.CreatorAuthoring.DTOs;
using StoryPlatform.Application.Features.ParentAuthoring;
using StoryPlatform.Contracts.AI.CreatorAssistance;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class ParentAssistanceTests
{
    private const string Text = "Mi tìm thấy chiếc hộp. Mẹ cùng Mi sửa lại góc hộp.";

    [Fact]
    public async Task AI_content_review_uses_unsaved_snapshot_and_rejects_replaced_source_version()
    {
        var f = new Fixture(); var (draft, request) = await f.Draft();
        var story = f.Store.Items<Story>().Single();
        story.Source = StorySource.Ai; story.Status = StoryStatus.ContentReview;
        f.Store.Items<StoryVersion>().Add(new() { Id = 12, StoryId = story.Id, IsCurrent = true, Title = "Bản lưu", Content = "Nội dung đã lưu" });
        request = request with { BaseVersionId = 12 };
        var task = await f.Service.CreateAsync(1, story.Id, request);
        await f.Service.SuggestAsync(1, story.Id, task.TaskId, new() { OperationId = "ai-options", ExpectedTaskRevision = task.TaskRevision });
        Assert.Equal(request.EditorSnapshot, f.Input!.Snapshot);
        Assert.NotNull(f.Input.ChildPolicy);
        Assert.Single(f.Store.Items<StoryVersion>());
        f.Store.Items<StoryVersion>().Single().IsCurrent = false;
        f.Store.Items<StoryVersion>().Add(new() { Id = 13, StoryId = story.Id, IsCurrent = true });
        var stale = await f.Service.GetAsync(1, story.Id, task.TaskId);
        Assert.True(stale.StoryContextStale);
        var error = await Assert.ThrowsAsync<AssistanceException>(() => f.Service.DirectionAsync(1, story.Id, task.TaskId,
            new() { OperationId = "stale-direction", ExpectedTaskRevision = stale.TaskRevision, OptionsStepId = stale.ActiveOptionsStepId!.Value, OptionId = "a" }));
        Assert.Equal("EDITOR_CONTEXT_STALE", error.Code);
    }

    [Theory]
    [InlineData(StoryStatus.MediaProcessing)]
    [InlineData(StoryStatus.Ready)]
    public async Task AI_assistance_does_not_reopen_finalized_pipeline(StoryStatus status)
    {
        var f = new Fixture(); var (draft, request) = await f.Draft();
        var story = f.Store.Items<Story>().Single(); story.Source = StorySource.Ai; story.Status = status;
        var error = await Assert.ThrowsAsync<AssistanceException>(() => f.Service.CreateAsync(1, draft.StoryId, request));
        Assert.Equal("AI_STORY_NOT_EDITABLE", error.Code); Assert.Equal(0, f.Calls);
    }

    [Theory]
    [InlineData(false, false, "CONSENT_REQUIRED")]
    [InlineData(true, true, "GENERATION_JOB_ACTIVE")]
    public async Task AI_assistance_preserves_consent_and_generation_guards(bool consent, bool running, string code)
    {
        var f = new Fixture(consent); var (draft, request) = await f.Draft();
        var story = f.Store.Items<Story>().Single(); story.Source = StorySource.Ai; story.Status = StoryStatus.ContentReview;
        if (running) f.Store.Items<StoryGenerationJob>().Add(new() { Id = 1, StoryId = story.Id, Status = GenerationJobStatus.Processing });
        var error = await Assert.ThrowsAsync<AssistanceException>(() => f.Service.CreateAsync(1, draft.StoryId, request));
        Assert.Equal(code, error.Code); Assert.Equal(0, f.Calls); Assert.Empty(f.Store.Items<StoryAssistanceTask>());
    }

    private sealed class Fixture
    {
        public ParentAuthoringTestStore Store { get; } = new();
        public ParentAssistanceService Service { get; }
        public int Calls { get; private set; }
        public AssistanceAiRequest? Input { get; private set; }
        public Func<AssistanceAiRequest, CancellationToken, Task<AssistanceAiResult>> Generate { get; set; } = (r, _) => Task.FromResult(
            r.Mode == "Trial" ? new AssistanceAiResult { ResultType = "Trial", Patch = new("ignored", 0, 0, "Mi rủ mẹ cùng thử một cách mới.", "") } :
            r.EntryPoint == "ReviewDraft" ? new AssistanceAiResult
            {
                ResultType = "ReviewFindings",
                ChecksPerformed = ["Continuity"],
                Findings = [new("f1", 0, 2, "Mi", "Làm rõ động cơ của nhân vật.", "Khám phá động cơ")]
            } : Options());
        public Fixture(bool consent = true, int timeout = 120)
        {
            var policy = Store.Items<SafetyPolicy>()[0];
            policy.ConsentRecorded = consent; policy.ConsentRecordedAt = consent ? DateTime.UtcNow : null;
            Store.Items<LearningProfile>().Add(new() { Id = 1, ChildProfileId = 3, ReadingLevel = 2 });
            Store.Items<ContentCategory>().Add(new() { Id = 1, Code = "violence", DisplayName = "bạo lực" });
            Store.Items<SafetyPolicyCategory>().Add(new() { Id = 1, SafetyPolicyId = 1, ContentCategoryId = 1, Rule = PolicyRule.Blocked });
            var ai = new Mock<ICreatorAssistanceAiClient>();
            ai.Setup(x => x.GenerateAsync(It.IsAny<AssistanceAiRequest>(), It.IsAny<CancellationToken>()))
                .Returns<AssistanceAiRequest, CancellationToken>((r, ct) => { Calls++; Input = r; return Generate(r, ct); });
            Service = new(Store.Uow.Object, ai.Object, new() { TimeoutSeconds = timeout }, TimeProvider.System,
                new ChildProfileAccessGuard(Store.Uow.Object), new(), NullLogger<ParentAssistanceService>.Instance);
        }
        public static AssistanceAiResult Options() => new() { ResultType = "Options", Options = [new("a", "Hợp tác", "Mi và mẹ cùng sửa hộp."), new("b", "Kiên trì", "Mi thử thêm cách mới.")] };
        public async Task<(CreatorDraftDto Draft, CreateAssistanceTaskRequest Request)> Draft(string entry = "ContinueWriting")
        {
            var draft = (await Store.Service.CreateParentDraftAsync(1, new() { OperationId = "draft", ChildProfileId = 3 }, default)).Story;
            return (draft, new()
            {
                OperationId = "task",
                RevisionToken = draft.RevisionToken,
                EntryPoint = entry,
                EditorSnapshot = new("Chiếc hộp của Mi", Text),
                Goal = "Gợi ý để tôi tự viết tiếp",
                Target = entry == "ContinueWriting" ? new("cursor", Text.Length, 0) : entry == "ReviewDraft" ? new("wholeDraft", 0, Text.Length) : new("selection", 0, 2)
            });
        }
        public async Task<int> Grant(CreatorDraftDto draft)
        {
            if (draft.SelectedVersionId is null)
                draft = (await Store.Service.SaveContentAsync(1, draft.StoryId, new() { OperationId = "v1", RevisionToken = draft.RevisionToken, Title = "Chiếc hộp", Content = Text })).Story;
            var check = await Store.Service.CheckAsync(1, draft.StoryId, new() { OperationId = "check", RevisionToken = draft.RevisionToken, BaseVersionId = draft.SelectedVersionId }, default);
            var confirmed = await Store.Service.ConfirmContentAsync(1, draft.StoryId, new()
            {
                OperationId = "confirm",
                RevisionToken = draft.RevisionToken,
                BaseVersionId = draft.SelectedVersionId,
                CheckId = check.CheckId,
                Checklist = new(true, true, true, true)
            }, default);
            var preparation = await Store.Service.PrepareAsync(1, draft.StoryId, new()
            {
                OperationId = "prep",
                RevisionToken = draft.RevisionToken,
                BaseVersionId = draft.SelectedVersionId,
                ContentConfirmationId = confirmed.ConfirmationId
            }, default);
            return (await Store.Service.ConfirmFamilyAsync(1, draft.StoryId, new()
            {
                OperationId = "grant",
                RevisionToken = draft.RevisionToken,
                BaseVersionId = draft.SelectedVersionId,
                PreparationId = preparation.PreparationId
            }, default)).ConfirmationId;
        }
    }

    [Theory]
    [InlineData("ContinueWriting")]
    [InlineData("SupportSelection")]
    [InlineData("ReviewDraft")]
    public async Task Three_entry_points_share_engine_and_only_save_creates_version(string entry)
    {
        var f = new Fixture(); var (draft, request) = await f.Draft(entry);
        var task = await f.Service.CreateAsync(1, draft.StoryId, request);
        Assert.Equal(task.TaskId, (await f.Service.CreateAsync(1, draft.StoryId, request)).TaskId);
        var command = new SuggestAssistanceRequest { OperationId = "suggest", ExpectedTaskRevision = task.TaskRevision };
        var suggestions = await f.Service.SuggestAsync(1, draft.StoryId, task.TaskId, command);
        await f.Service.SuggestAsync(1, draft.StoryId, task.TaskId, command);
        Assert.Equal(1, f.Calls); Assert.Empty(f.Store.Items<StoryVersion>());
        Assert.NotNull(f.Input!.ChildPolicy); Assert.Equal(2, f.Input.ChildPolicy.ReadingLevel);
        Assert.Contains("violence: bạo lực", f.Input.ChildPolicy.BlockedCategories);
        if (entry == "ReviewDraft")
        {
            Assert.Equal("ReviewReady", suggestions.Status);
            var child = await f.Service.CreateAsync(1, draft.StoryId, request with
            {
                OperationId = "finding-task",
                EntryPoint = "SupportSelection",
                Goal = "",
                ParentTaskId = task.TaskId,
                SourceReviewStepId = suggestions.StepId,
                FindingId = "f1",
                Target = new("selection", 0, 2)
            });
            Assert.Equal("Khám phá động cơ", child.Goal);
            return;
        }
        var direction = await f.Service.DirectionAsync(1, draft.StoryId, task.TaskId, new()
        {
            OperationId = "direction",
            ExpectedTaskRevision = suggestions.TaskRevision,
            OptionsStepId = suggestions.StepId,
            OptionId = "a"
        });
        var trial = await f.Service.TrialAsync(1, draft.StoryId, task.TaskId, new()
        {
            OperationId = "trial",
            ExpectedTaskRevision = direction.TaskRevision,
            DirectionStepId = direction.StepId
        });
        Assert.Empty(f.Store.Items<StoryVersion>());
        var saved = await f.Store.Service.SaveContentAsync(1, draft.StoryId, new()
        {
            OperationId = "save",
            RevisionToken = draft.RevisionToken,
            Title = "Chiếc hộp",
            Content = Text + " Mi đã tìm được cách mới.",
            AssistanceReferences = [new(task.TaskId, direction.StepId, "DirectionUsed"), new(task.TaskId, trial.StepId, "TrialUsed")]
        });
        Assert.Single(f.Store.Items<StoryVersion>());
        Assert.Contains("newReferences", f.Store.Items<StoryVersion>()[0].AuthoringMetadata.AssistanceProvenanceJson);
        Assert.Equal(StorySource.Manual, f.Store.Items<Story>()[0].Source);
        Assert.Empty(f.Store.Items<StoryGenerationRequest>()); Assert.Empty(f.Store.Items<StoryGenerationJob>());
    }

    [Fact]
    public async Task Clarification_is_a_choice_not_a_chat_transcript()
    {
        var f = new Fixture(); var (draft, req) = await f.Draft();
        f.Generate = (r, _) => Task.FromResult(r.Answer is null ? new AssistanceAiResult
        {
            ResultType = "Clarification",
            Question = new("q", "Bạn muốn nhấn mạnh điều gì?", [new("a", "Hợp tác"), new("b", "Kiên trì")])
        } : Fixture.Options());
        var task = await f.Service.CreateAsync(1, draft.StoryId, req);
        var question = await f.Service.SuggestAsync(1, draft.StoryId, task.TaskId, new() { OperationId = "ask", ExpectedTaskRevision = task.TaskRevision });
        var answer = await f.Service.AnswerAsync(1, draft.StoryId, task.TaskId, new()
        {
            OperationId = "answer",
            ExpectedTaskRevision = question.TaskRevision,
            ClarificationStepId = question.StepId,
            QuestionId = "q",
            ChoiceId = "a"
        });
        Assert.Equal(1, f.Calls);
        var options = await f.Service.SuggestAsync(1, draft.StoryId, task.TaskId, new() { OperationId = "options", ExpectedTaskRevision = answer.TaskRevision, AnswerStepId = answer.StepId });
        Assert.Equal("OptionsReady", options.Status); Assert.Equal("Hợp tác", f.Input!.Answer);
    }

    [Fact]
    public async Task Missing_consent_disables_AI_but_manual_save_is_available()
    {
        var f = new Fixture(consent: false); var (draft, request) = await f.Draft();
        var detail = await f.Store.Service.GetParentDraftAsync(1, draft.StoryId, default);
        Assert.False(detail.CanUseAiAssistance); Assert.Equal("CONSENT_REQUIRED", detail.AiUnavailableReason);
        var error = await Assert.ThrowsAsync<AssistanceException>(() => f.Service.CreateAsync(1, draft.StoryId, request));
        Assert.Equal("CONSENT_REQUIRED", error.Code); Assert.Equal(0, f.Calls);
        await f.Store.Service.SaveContentAsync(1, draft.StoryId, new() { OperationId = "save", RevisionToken = draft.RevisionToken, Content = Text });
        Assert.Single(f.Store.Items<StoryVersion>());
    }

    [Theory]
    [InlineData("policy")]
    [InlineData("consent")]
    [InlineData("permission")]
    [InlineData("child-language")]
    [InlineData("account")]
    public async Task Context_changed_while_AI_runs_records_failure_and_releases_operation(string change)
    {
        var f = new Fixture(); var (draft, request) = await f.Draft();
        // FindAsync/GetById can return EF's cached tracked values for the life of the request.
        // Policy revalidation must query fresh rows after the provider wait instead.
        Mock.Get(f.Store.Uow.Object.Repository<ChildProfile>()).Setup(x => x.GetByIdAsync(3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChildProfile { Id = 3, OwnerUserId = 1, Status = ChildProfileStatus.Active, Language = "vi", AgeBand = AgeBand.Age_6_8 });
        Mock.Get(f.Store.Uow.Object.Repository<UserAccount>()).Setup(x => x.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAccount { Id = 1, Role = UserRole.User, Status = AccountStatus.Active });
        var task = await f.Service.CreateAsync(1, draft.StoryId, request);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = new TaskCompletionSource<AssistanceAiResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Generate = (_, _) => { entered.SetResult(); return result.Task; };
        var running = f.Service.SuggestAsync(1, draft.StoryId, task.TaskId, new() { OperationId = "suggest", ExpectedTaskRevision = task.TaskRevision });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        switch (change)
        {
            case "policy": f.Store.Items<SafetyPolicy>()[0].MaxStoryLength++; break;
            case "consent": f.Store.Items<SafetyPolicy>()[0].ConsentRecorded = false; break;
            case "permission":
                f.Store.Items<ChildProfile>()[0].OwnerUserId = 99;
                Mock.Get(f.Store.Uow.Object.Repository<ChildProfile>())
                    .Setup(x => x.GetByIdAsync(3, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new ChildProfile { Id = 3, OwnerUserId = 99, Status = ChildProfileStatus.Active });
                break;
            case "child-language": f.Store.Items<ChildProfile>()[0].Language = "en"; break;
            case "account": f.Store.Items<UserAccount>()[0].Status = AccountStatus.Suspended; break;
        }
        result.SetResult(Fixture.Options());
        await Assert.ThrowsAsync<AssistanceException>(() => running);
        Assert.Null(f.Store.Items<StoryAssistanceTask>()[0].ActiveOperationId);
        Assert.Equal("Failed", Assert.Single(f.Store.Items<StoryAssistanceStep>()).Status);
        Assert.Null(f.Store.Items<StoryAssistanceStep>()[0].OutputJson);
        Assert.Empty(f.Store.Items<StoryVersion>());
    }

    [Fact]
    public async Task Parent_and_creator_routes_cannot_cross_scopes_or_reuse_another_users_task()
    {
        var f = new Fixture(); var (draft, request) = await f.Draft();
        var task = await f.Service.CreateAsync(1, draft.StoryId, request);
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GetAsync(2, draft.StoryId, task.TaskId));
        var creator = new CreatorAssistanceService(f.Store.Uow.Object, Mock.Of<ICreatorAssistanceAiClient>(), new(), TimeProvider.System);
        await Assert.ThrowsAsync<NotFoundException>(() => creator.GetAsync(1, draft.StoryId, task.TaskId));
        f.Store.Items<Story>()[0].Scope = StoryScope.Creator;
        await Assert.ThrowsAsync<NotFoundException>(() => f.Service.GetAsync(1, draft.StoryId, task.TaskId));
    }

    [Fact]
    public async Task Legacy_and_conflicting_pipeline_traces_are_not_adopted_by_new_editor()
    {
        var f = new Fixture(); var (draft, _) = await f.Draft();
        f.Store.Items<StoryGenerationRequest>().Add(new() { Id = 1, StoryId = draft.StoryId });
        await Assert.ThrowsAsync<ConflictException>(() => f.Store.Service.GetParentDraftAsync(1, draft.StoryId, default));
        Assert.Empty((await f.Store.Service.GetMineAsync(1, 1, 20)).Items);
        f.Store.Items<Story>()[0].CreateOperationId = null;
        await Assert.ThrowsAsync<NotFoundException>(() => f.Store.Service.GetParentDraftAsync(1, draft.StoryId, default));
    }

    [Fact]
    public async Task Policy_change_marks_task_stale_and_blocks_references_without_blocking_manual_writing()
    {
        var f = new Fixture(); var (draft, req) = await f.Draft();
        var task = await f.Service.CreateAsync(1, draft.StoryId, req);
        var options = await f.Service.SuggestAsync(1, draft.StoryId, task.TaskId, new() { OperationId = "suggest", ExpectedTaskRevision = task.TaskRevision });
        var direction = await f.Service.DirectionAsync(1, draft.StoryId, task.TaskId, new()
        {
            OperationId = "direction",
            ExpectedTaskRevision = options.TaskRevision,
            OptionsStepId = options.StepId,
            OptionId = "a"
        });
        f.Store.Items<SafetyPolicy>()[0].MaxStoryLength++;
        Assert.True((await f.Service.GetAsync(1, draft.StoryId, task.TaskId)).AudienceContextStale);
        var save = new SaveCreatorContentRequest
        {
            OperationId = "save",
            RevisionToken = draft.RevisionToken,
            Content = Text,
            AssistanceReferences = [new(task.TaskId, direction.StepId, "DirectionUsed")]
        };
        var error = await Assert.ThrowsAsync<AssistanceException>(() => f.Store.Service.SaveContentAsync(1, draft.StoryId, save));
        Assert.Equal("AUDIENCE_CONTEXT_STALE", error.Code); Assert.Empty(f.Store.Items<StoryVersion>());
        await f.Store.Service.SaveContentAsync(1, draft.StoryId, save with { AssistanceReferences = [] });
        Assert.Single(f.Store.Items<StoryVersion>());
    }

    [Fact]
    public async Task Inflight_replay_returns_parent_poll_url_and_cancel_does_not_create_versions()
    {
        var f = new Fixture(); var (draft, req) = await f.Draft();
        var task = await f.Service.CreateAsync(1, draft.StoryId, req);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Generate = async (_, ct) => { entered.SetResult(); await Task.Delay(Timeout.Infinite, ct); return Fixture.Options(); };
        var command = new SuggestAssistanceRequest { OperationId = "suggest", ExpectedTaskRevision = task.TaskRevision };
        var running = f.Service.SuggestAsync(1, draft.StoryId, task.TaskId, command);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var replay = await f.Service.SuggestAsync(1, draft.StoryId, task.TaskId, command);
        Assert.True(replay.IsRunning); Assert.Equal($"/api/v1/parent/stories/{draft.StoryId}/assistance-tasks/{task.TaskId}", replay.PollUrl);
        await f.Service.CancelAsync(1, draft.StoryId, task.TaskId, new() { OperationId = "cancel", ExpectedTaskRevision = replay.TaskRevision, TargetOperationId = "suggest" });
        var cancelled = await running;
        Assert.Equal("Cancelled", cancelled.Step.Status); Assert.Equal(1, f.Calls);
        Assert.Empty(f.Store.Items<StoryVersion>());
    }

    [Fact]
    public async Task Timeout_receipt_is_durable_and_retry_is_explicit()
    {
        var f = new Fixture(timeout: 1); var (draft, req) = await f.Draft();
        var task = await f.Service.CreateAsync(1, draft.StoryId, req);
        f.Generate = async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return Fixture.Options(); };
        var command = new SuggestAssistanceRequest { OperationId = "timeout", ExpectedTaskRevision = task.TaskRevision };
        var error = await Assert.ThrowsAsync<AssistanceException>(() => f.Service.SuggestAsync(1, draft.StoryId, task.TaskId, command));
        Assert.Equal("AI_TIMEOUT", error.Code);
        var replay = await Assert.ThrowsAsync<AssistanceException>(() => f.Service.SuggestAsync(1, draft.StoryId, task.TaskId, command));
        Assert.Equal(504, replay.StatusCode); Assert.Equal(1, f.Calls);
        Assert.Null((await f.Service.GetAsync(1, draft.StoryId, task.TaskId)).ActiveOperation);
    }

    [Fact]
    public async Task Finalized_story_blocks_all_writes_but_allows_receipts_history_and_closing()
    {
        var f = new Fixture(); var (draft, req) = await f.Draft();
        var task = await f.Service.CreateAsync(1, draft.StoryId, req);
        var save = new SaveCreatorContentRequest { OperationId = "save", RevisionToken = draft.RevisionToken, Title = "Chiếc hộp", Content = Text };
        var saved = await f.Store.Service.SaveContentAsync(1, draft.StoryId, save);
        var preview = await f.Store.Service.PreviewImportAsync(1, draft.StoryId, new() { OperationId = "preview", RevisionToken = saved.Story.RevisionToken, Content = Text });
        var grant = await f.Grant(saved.Story);
        Assert.Equal(saved.Version.VersionId, (await f.Store.Service.SaveContentAsync(1, draft.StoryId, save)).Version.VersionId);
        await Assert.ThrowsAsync<ConflictException>(() => f.Store.Service.SaveContentAsync(1, draft.StoryId,
            save with { OperationId = "blocked-save", RevisionToken = saved.Story.RevisionToken, BaseVersionId = saved.Version.VersionId }));
        await Assert.ThrowsAsync<ConflictException>(() => f.Store.Service.RestoreVersionAsync(1, draft.StoryId, new()
        {
            OperationId = "restore",
            RevisionToken = saved.Story.RevisionToken,
            BaseVersionId = saved.Version.VersionId,
            SourceVersionId = saved.Version.VersionId
        }));
        await Assert.ThrowsAsync<ConflictException>(() => f.Store.Service.ConfirmImportAsync(1, draft.StoryId, new()
        {
            OperationId = "import",
            RevisionToken = saved.Story.RevisionToken,
            BaseVersionId = saved.Version.VersionId,
            PreviewToken = preview.PreviewToken,
            Title = "Chiếc hộp",
            Content = preview.Content,
            SourceDeclaration = "Tự viết",
            RightsDeclaration = "Tôi có quyền"
        }));
        await f.Service.CloseAsync(1, draft.StoryId, task.TaskId, new() { OperationId = "close", ExpectedTaskRevision = task.TaskRevision });
        Assert.Single(await f.Store.Service.GetVersionsAsync(1, draft.StoryId));
        Assert.NotNull(await f.Store.Service.ReadChildPackageAsync(3, grant, default));
    }

    [Fact]
    public async Task Implicitly_unreadable_grant_stays_locked_until_every_confirmation_is_explicitly_revoked()
    {
        var f = new Fixture(); var (draft, _) = await f.Draft();
        var grantId = await f.Grant(draft);
        var story = (await f.Store.Service.GetParentDraftAsync(1, draft.StoryId, default)).Story;
        var grant = f.Store.Items<ParentStoryOperation>().Single(x => x.Id == grantId);
        // A historical duplicate grant must not let revoking just one reopen editing.
        f.Store.Items<ParentStoryOperation>().Add(new()
        {
            Id = 999,
            StoryId = grant.StoryId,
            ActorUserId = grant.ActorUserId,
            ChildProfileId = grant.ChildProfileId,
            StoryVersionId = grant.StoryVersionId,
            Kind = "FamilyConfirmation",
            ResultJson = grant.ResultJson
        });
        f.Store.Items<SafetyPolicy>()[0].MaxStoryLength++;
        Assert.Empty(await f.Store.Service.ChildLibraryAsync(3, default));
        Assert.True((await f.Store.Service.GetParentDraftAsync(1, draft.StoryId, default)).IsReadOnly);
        await f.Store.Service.RevokeFamilyAsync(1, draft.StoryId, new()
        {
            OperationId = "revoke-1",
            RevisionToken = story.RevisionToken,
            BaseVersionId = story.SelectedVersionId,
            ConfirmationId = grantId
        }, default);
        Assert.True((await f.Store.Service.GetParentDraftAsync(1, draft.StoryId, default)).IsReadOnly);
        await f.Store.Service.RevokeFamilyAsync(1, draft.StoryId, new()
        {
            OperationId = "revoke-2",
            RevisionToken = story.RevisionToken,
            BaseVersionId = story.SelectedVersionId,
            ConfirmationId = 999
        }, default);
        var reopened = await f.Store.Service.GetParentDraftAsync(1, draft.StoryId, default);
        Assert.False(reopened.IsReadOnly); Assert.NotEqual(story.RevisionToken, reopened.Story.RevisionToken);
    }

    [Fact]
    public async Task Grant_during_inference_prevents_late_success_and_revocation_invalidates_old_tasks()
    {
        var f = new Fixture(); var (draft, _) = await f.Draft();
        var saved = await f.Store.Service.SaveContentAsync(1, draft.StoryId, new() { OperationId = "save", RevisionToken = draft.RevisionToken, Title = "Chiếc hộp", Content = Text });
        var req = new CreateAssistanceTaskRequest
        {
            OperationId = "task",
            RevisionToken = saved.Story.RevisionToken,
            BaseVersionId = saved.Version.VersionId,
            EditorSnapshot = new("Chiếc hộp", Text),
            Target = new("cursor", Text.Length, 0)
        };
        var task = await f.Service.CreateAsync(1, draft.StoryId, req);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = new TaskCompletionSource<AssistanceAiResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Generate = (_, _) => { entered.SetResult(); return result.Task; };
        var running = f.Service.SuggestAsync(1, draft.StoryId, task.TaskId, new() { OperationId = "suggest", ExpectedTaskRevision = task.TaskRevision });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var grantId = await f.Grant(saved.Story);
        result.SetResult(Fixture.Options());
        await Assert.ThrowsAsync<AssistanceException>(() => running);
        Assert.Null(f.Store.Items<StoryAssistanceTask>()[0].ActiveOperationId);
        Assert.True((await f.Store.Service.GetParentDraftAsync(1, draft.StoryId, default)).IsReadOnly);
        await Assert.ThrowsAsync<ConflictException>(() => f.Service.CreateAsync(1, draft.StoryId, req with { OperationId = "another-task" }));
        await f.Store.Service.RevokeFamilyAsync(1, draft.StoryId, new()
        {
            OperationId = "revoke",
            RevisionToken = saved.Story.RevisionToken,
            BaseVersionId = saved.Version.VersionId,
            ConfirmationId = grantId
        }, default);
        var reopened = await f.Store.Service.GetParentDraftAsync(1, draft.StoryId, default);
        Assert.False(reopened.IsReadOnly); Assert.NotEqual(saved.Story.RevisionToken, reopened.Story.RevisionToken);
        await Assert.ThrowsAsync<AssistanceException>(() => f.Service.SuggestAsync(1, draft.StoryId, task.TaskId, new() { OperationId = "stale", ExpectedTaskRevision = 3 }));
        await Assert.ThrowsAsync<ConflictException>(() => f.Store.Service.PreviewAsync(1, draft.StoryId,
            f.Store.Items<ParentStoryOperation>().Single(x => x.Kind == "Preparation").Id, default));
        Assert.Single(f.Store.Items<StoryVersion>());
    }
}
