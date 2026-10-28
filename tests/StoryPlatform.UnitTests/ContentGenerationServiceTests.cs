using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StoryPlatform.Application.Abstractions.AI;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.AIStoryInput.Models;
using StoryPlatform.Application.Features.ContentGeneration.Interfaces;
using StoryPlatform.Application.Features.ContentGeneration;
using StoryPlatform.Application.Features.ContentGeneration.DTOs;
using StoryPlatform.Application.Features.ContentGeneration.Quality;
using StoryPlatform.Application.Features.ContentGeneration.Services;
using StoryPlatform.Application.Features.ExistingStories.Interfaces;
using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Contracts.AI.Responses;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class ContentGenerationServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Worker_logs_handled_quiz_failure_without_story_or_provider_payload(bool providerFailure)
    {
        var store=Seed();var logger=new JobLogger();var client=new InvalidQuizAIClient(providerFailure);
        var service=new ContentGenerationService(store,client,new PassingQualityEvaluator(),new FakeFailureFinalizer(store),
            new RecordingHandoffService(store),new ContentGenerationOptions(),logger:logger);
        for(var i=0;i<3;i++)Assert.True(await service.ProcessNextAsync());
        var quiz=store.Items<StoryGenerationJob>().Single(x=>x.Operation==GenerationJobOperation.GenerateQuiz);
        Assert.Equal(GenerationJobStatus.Failed,quiz.Status);
        Assert.Equal(providerFailure?"QUIZ_GENERATION_FAILED":"QUIZ_VALIDATION_FAILED",quiz.ErrorCode);
        var entry=Assert.Single(logger.Errors);
        Assert.Contains($"JobId={quiz.Id}",entry);Assert.Contains("Operation=GenerateQuiz",entry);
        if(!providerFailure){Assert.Contains("ValidationCode=QUIZ_OPTIONS_INVALID",entry);Assert.Contains("ItemIndex=0",entry);}
        Assert.DoesNotContain("private-story-payload",entry);
        Assert.DoesNotContain("Lan và Minh",entry);
    }

    private sealed class JobLogger:ILogger<ContentGenerationService>
    {
        public List<string> Errors {get;}=[];
        public IDisposable? BeginScope<TState>(TState state) where TState:notnull=>null;
        public bool IsEnabled(LogLevel level)=>true;
        public void Log<TState>(LogLevel level,EventId id,TState state,Exception? exception,Func<TState,Exception?,string> format)
        {if(level==LogLevel.Error)Errors.Add(format(state,exception));}
    }

    private sealed class InvalidQuizAIClient(bool providerFailure):FakeAIClient
    {
        public override async Task<GenerateQuizResponse> GenerateQuizAsync(GenerateQuizRequest request,CancellationToken cancellationToken=default)
        {
            if(providerFailure)throw new InvalidOperationException("private-story-payload");
            var response=await base.GenerateQuizAsync(request,cancellationToken);
            return response with {Items=response.Items.Select((item,i)=>i==0?item with {CorrectOptionIndex=99}:item).ToArray()};
        }
    }
    [Fact]
    public async Task Progress_only_uses_failed_artifacts_of_the_current_stable_version()
    {
        var store=Seed();var version=store.Items<StoryVersion>().Single();version.Content="Nội dung ổn định";
        store.Items<StoryGenerationJob>().Single().Status=GenerationJobStatus.Completed;
        store.Seed(new StoryGenerationJob {Id=90,StoryId=1,StoryVersionId=version.Id,Operation=GenerationJobOperation.GenerateVocabulary,Status=GenerationJobStatus.Failed,ErrorCode="CURRENT_FAILURE"});
        store.Seed(new StoryGenerationJob {Id=100,StoryId=1,StoryVersionId=999,Operation=GenerationJobOperation.GenerateVocabulary,Status=GenerationJobStatus.Failed,ErrorCode="OLD_VERSION_FAILURE"});
        var progress=await Service(store,new FakeAIClient()).GetProgressAsync(1,1);
        Assert.Equal("failed_vocabulary",progress.CurrentStep);Assert.Equal("CURRENT_FAILURE",progress.LastErrorCode);
    }
    [Fact]
    public async Task Progress_clears_historical_failure_after_successful_retry()
    {
        var store=Seed();var service=Service(store,new FakeAIClient());
        store.Seed(new StoryGenerationJob {Id=0,StoryId=1,Operation=GenerationJobOperation.GenerateContent,Status=GenerationJobStatus.Failed,ErrorCode="OLD_FAILURE"});
        for(var i=0;i<4;i++)await service.ProcessNextAsync();
        var progress=await service.GetProgressAsync(1,1);
        Assert.True(progress.IsComplete);Assert.Equal("complete",progress.CurrentStep);Assert.Null(progress.LastErrorCode);Assert.Null(progress.QualityFailure);
    }

    [Theory]
    [InlineData(GenerationJobStatus.Pending)]
    [InlineData(GenerationJobStatus.Processing)]
    public async Task Progress_does_not_fail_while_retry_is_active(GenerationJobStatus status)
    {
        var store=Seed();var first=store.Items<StoryGenerationJob>().Single();first.Status=GenerationJobStatus.Failed;first.ErrorCode="OLD_FAILURE";
        store.Seed(new StoryGenerationJob {Id=first.Id+1,StoryId=1,Operation=GenerationJobOperation.GenerateContent,Status=status});
        var progress=await Service(store,new FakeAIClient()).GetProgressAsync(1,1);
        Assert.StartsWith(status==GenerationJobStatus.Pending?"pending_":"generating_",progress.CurrentStep);
        Assert.Null(progress.LastErrorCode);Assert.Null(progress.QualityFailure);Assert.False(progress.IsComplete);
    }
    [Theory]
    [InlineData(StoryOutputMode.ReadingMediaOnly, 1)]
    [InlineData(StoryOutputMode.Learning, 4)]
    public async Task Creator_GenerationReachesContentReviewForBothOutputModes(StoryOutputMode mode, int jobs)
    {
        var store = Seed();
        store.Seed(new UserAccount { Id = 1, Role = UserRole.ContentCreator, Status = AccountStatus.Active });
        var story = store.Items<Story>().Single();
        story.OutputMode = mode;
        CreatorGenerationTestContext.Configure(story, store.Items<StoryGenerationRequest>().Single());
        var handoff = new StoryPlatform.Application.Features.ExistingStories.Services.StableVersionArtifactHandoffService(store,
            new Moq.Mock<StoryPlatform.Application.Features.ChildProfiles.Learning.Interfaces.ILearningProfileService>(Moq.MockBehavior.Strict).Object,
            new Moq.Mock<StoryPlatform.Application.Features.ChildProfiles.Safety.Interfaces.ISafetyPolicyService>(Moq.MockBehavior.Strict).Object);
        var service = new ContentGenerationService(store, new FakeAIClient(), new PassingQualityEvaluator(),
            new FakeFailureFinalizer(store), handoff, new ContentGenerationOptions());
        for (var i = 0; i < jobs; i++) Assert.True(await service.ProcessNextAsync());
        Assert.Equal(StoryStatus.ContentReview, story.Status);
        Assert.False(story.IsPublished);
        Assert.Null(story.ChildProfileId);
        await service.GetProgressAsync(1, 1);
        await Assert.ThrowsAsync<StoryPlatform.Application.Common.Exceptions.NotFoundException>(() => service.GetProgressAsync(99, 1));
    }

    [Fact]
    public async Task ReadingMediaOnly_ProgressMarksLearningArtifactsSkippedAfterStableContent()
    {
        var store = Seed();
        var story = store.Items<Story>().Single();
        story.OutputMode = StoryOutputMode.ReadingMediaOnly;
        story.Status = StoryStatus.ContentReview;
        store.Items<StoryVersion>().Single().Content = "Lan và Minh cùng chia sẻ một quyển sách.";
        store.Items<StoryGenerationJob>().Single().Status = GenerationJobStatus.Completed;

        var progress = await Service(store, new FakeAIClient()).GetProgressAsync(1, story.Id);

        Assert.Equal("reading_media_only", progress.OutputMode);
        Assert.True(progress.IsComplete);
        Assert.Equal("skipped", progress.Vocabulary);
        Assert.Equal("skipped", progress.Quiz);
        Assert.Equal("skipped", progress.Discussion);
    }

    [Fact]
    public async Task Stable_content_does_not_finish_progress_while_generation_is_still_processing()
    {
        var store=Seed();var story=store.Items<Story>().Single();story.OutputMode=StoryOutputMode.ReadingMediaOnly;
        story.Status=StoryStatus.ContentReview;store.Items<StoryVersion>().Single().Content="Nội dung bản trước";
        store.Items<StoryGenerationJob>().Single().Status=GenerationJobStatus.Processing;
        var progress=await Service(store,new FakeAIClient()).GetProgressAsync(1,1);
        Assert.False(progress.IsComplete);Assert.Equal("generating_content",progress.CurrentStep);Assert.Null(progress.LastErrorCode);
    }

    [Fact]
    public async Task Happy_path_runs_story_then_vocabulary_quiz_discussion()
    {
        var store = Seed();
        var ai = new FakeAIClient();
        var service = Service(store, ai);

        Assert.True(await service.ProcessNextAsync());
        Assert.Equal(GenerationJobOperation.GenerateVocabulary, PendingJob(store).Operation);
        Assert.True(await service.ProcessNextAsync());
        Assert.Equal(GenerationJobOperation.GenerateQuiz, PendingJob(store).Operation);
        Assert.True(await service.ProcessNextAsync());
        Assert.Equal(GenerationJobOperation.GenerateDiscussion, PendingJob(store).Operation);
        Assert.True(await service.ProcessNextAsync());

        var story = store.Items<Story>().Single();
        var stable = store.Items<StoryVersion>().Single(item => item.Content is not null);
        Assert.Equal(StoryStatus.ContentReview, story.Status);
        Assert.Equal("Một câu chuyện về tình bạn và sự sẻ chia.", story.Description);
        Assert.True(stable.IsCurrent);
        Assert.NotNull(stable.ReadabilityFkgl);
        Assert.NotNull(stable.ReadabilityFre);
        Assert.False(store.Items<StoryVersion>().Single(item => item.Content is null).IsCurrent);
        Assert.Single(store.Items<StoryVocabulary>());
        Assert.Equal(3, store.Items<QuizItem>().Count);
        Assert.Single(store.Items<DiscussionQuestion>());
        Assert.Equal(new[] { "content", "safety", "vocabulary", "quiz", "discussion" }, ai.Calls);

        var progress = await service.GetProgressAsync(1, story.Id);
        Assert.True(progress.IsComplete);
        Assert.Equal("complete", progress.CurrentStep);
        Assert.Equal(stable.Id, progress.StableStoryVersionId);
    }

    [Fact]
    public async Task Existing_story_reaches_content_review_after_artifact_chain_completes()
    {
        var store = Seed();
        var story = store.Items<Story>().Single();
        story.Source = StorySource.Manual;
        story.Status = StoryStatus.Draft;
        var version = store.Items<StoryVersion>().Single();
        version.Content = "Lan và Minh cùng chia sẻ một quyển sách.";
        version.Lesson = "Biết chia sẻ";
        var job = store.Items<StoryGenerationJob>().Single();
        job.Operation = GenerationJobOperation.GenerateVocabulary;
        job.Stage = JobStage.ContentArtifactPending;
        job.StoryVersionId = version.Id;
        job.BaseStoryVersionId = version.Id;
        var service = Service(store, new FakeAIClient());

        Assert.True(await service.ProcessNextAsync());
        Assert.True(await service.ProcessNextAsync());
        Assert.True(await service.ProcessNextAsync());

        Assert.Equal(StoryStatus.ContentReview, story.Status);
        Assert.DoesNotContain(store.Items<StoryGenerationJob>(), item =>
            item.Status is GenerationJobStatus.Pending or GenerationJobStatus.Processing);
    }

    [Fact]
    public async Task Invalid_vocabulary_retries_only_vocabulary()
    {
        var store = Seed();
        var ai = new FakeAIClient(invalidFirstVocabulary: true);
        var service = Service(store, ai);
        await service.ProcessNextAsync();
        await service.ProcessNextAsync();

        Assert.Equal(2, ai.Calls.Count(item => item == "vocabulary"));
        Assert.Equal(1, ai.Calls.Count(item => item == "content"));
        Assert.Single(store.Items<StoryVocabulary>());
        Assert.Equal(GenerationJobOperation.GenerateQuiz, PendingJob(store).Operation);
    }

    [Fact]
    public async Task Refinable_content_creates_new_immutable_version_before_stable_promotion()
    {
        var store = Seed();
        var ai = new FakeAIClient();
        var service = new ContentGenerationService(store, ai, new FailOnceQualityEvaluator(), new FakeFailureFinalizer(store), new RecordingHandoffService(store), new ContentGenerationOptions());

        Assert.True(await service.ProcessNextAsync());

        var versions = store.Items<StoryVersion>().OrderBy(item => item.VersionNo).ToArray();
        Assert.Equal(3, versions.Length);
        Assert.Null(versions[0].Content);
        Assert.False(versions[1].IsCurrent);
        Assert.Equal(VersionEditType.Initial, versions[1].EditType);
        Assert.True(versions[2].IsCurrent);
        Assert.Equal(VersionEditType.AiRefined, versions[2].EditType);
        Assert.Equal(new[] { "content", "safety", "refine", "safety" }, ai.Calls);
    }

    [Fact]
    public async Task Reclaimed_job_resumes_persisted_candidate_without_generating_duplicate()
    {
        var store = Seed();
        var job = store.Items<StoryGenerationJob>().Single();
        job.Status = GenerationJobStatus.Processing;
        job.Stage = JobStage.ContentGenerating;
        job.LeaseExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        job.AttemptNo = 1;
        job.StoryVersionId = 2;
        store.Seed(new StoryVersion
        {
            Id = 2,
            StoryId = 1,
            VersionNo = 2,
            EditType = VersionEditType.Initial,
            Title = "Tình bạn",
            OutlineOpening = "Lan gặp Minh",
            OutlineDevelopment = "Hai bạn giúp nhau",
            OutlineEnding = "Hai bạn biết chia sẻ",
            Content = "Lan và Minh cùng chia sẻ một quyển sách.",
            Lesson = "Biết chia sẻ",
            IsCurrent = false
        });
        var ai = new FakeAIClient();

        Assert.True(await Service(store, ai).ProcessNextAsync());

        Assert.Equal(["safety"], ai.Calls);
        Assert.Equal(2, store.Items<StoryVersion>().Count);
        Assert.True(store.Items<StoryVersion>().Single(item => item.Id == 2).IsCurrent);
        Assert.Equal(GenerationJobOperation.GenerateVocabulary, PendingJob(store).Operation);
    }

    [Fact]
    public async Task Semantic_hard_safety_rejection_stops_before_vocabulary()
    {
        var store = Seed();
        var ai = new FakeAIClient(blockSafety: true);

        Assert.True(await Service(store, ai).ProcessNextAsync());

        var job = store.Items<StoryGenerationJob>().Single();
        Assert.Equal(GenerationJobStatus.Failed, job.Status);
        Assert.Equal("CONTENT_SAFETY_BLOCKED", job.ErrorCode);
        Assert.DoesNotContain(store.Items<StoryGenerationJob>(), item => item.Operation == GenerationJobOperation.GenerateVocabulary);
        Assert.False(store.Items<StoryVersion>().Single(item => item.Content is not null).IsCurrent);
    }

    [Fact]
    public async Task Safety_score_below_profile_threshold_stops_before_vocabulary()
    {
        var store = Seed(safetyScoreThreshold: 90m);
        var ai = new FakeAIClient(safetyScore: 75d);

        Assert.True(await Service(store, ai).ProcessNextAsync());

        var job = store.Items<StoryGenerationJob>().Single();
        Assert.Equal(GenerationJobStatus.Failed, job.Status);
        Assert.Equal("CONTENT_SAFETY_SCORE_NOT_MET", job.ErrorCode);
        Assert.DoesNotContain(store.Items<StoryGenerationJob>(), item => item.Operation == GenerationJobOperation.GenerateVocabulary);
    }

    [Fact]
    public async Task Failed_content_progress_returns_specific_quality_gate_details()
    {
        var store = Seed();
        var service = new ContentGenerationService(
            store,
            new FakeAIClient(),
            new AlwaysFailReadabilityQualityEvaluator(),
            new FakeFailureFinalizer(store),
            new RecordingHandoffService(store),
            new ContentGenerationOptions { MaxContentRefinementAttempts = 2 });

        Assert.True(await service.ProcessNextAsync());

        var progress = await service.GetProgressAsync(1, 1);
        Assert.Equal("failed_content", progress.CurrentStep);
        Assert.Equal("failed", progress.Content);
        Assert.Equal("CONTENT_QUALITY_NOT_MET", progress.LastErrorCode);
        Assert.NotNull(progress.QualityFailure);
        Assert.Equal(2, progress.QualityFailure.RefinementAttempts);
        var gate = Assert.Single(progress.QualityFailure.FailedGates);
        Assert.Equal("readability", gate.Gate);
        Assert.Equal("CONTENT_READABILITY_NOT_MET", gate.ReasonCode);
        Assert.Equal("Câu quá dài.", Assert.Single(gate.Violations));
    }

    [Fact]
    public async Task Retry_failed_content_generation_queues_new_job_and_preserves_failure_history()
    {
        var store = Seed();
        var failed = store.Items<StoryGenerationJob>().Single();
        failed.Status = GenerationJobStatus.Failed;
        failed.Stage = JobStage.ContentFailed;
        failed.ErrorCode = "GEMINI_API_ERROR";
        failed.CompletedAt = DateTime.UtcNow;
        var service = Service(store, new FakeAIClient());

        var progress = await service.RetryAsync(1, 1, new RetryContentGenerationRequestDto
        {
            RetryKey = "retry-content-0001"
        });

        var jobs = store.Items<StoryGenerationJob>().OrderBy(item => item.Id).ToArray();
        Assert.Equal(2, jobs.Length);
        Assert.Equal(GenerationJobStatus.Failed, jobs[0].Status);
        Assert.Equal("GEMINI_API_ERROR", jobs[0].ErrorCode);
        Assert.Equal(GenerationJobStatus.Pending, jobs[1].Status);
        Assert.Equal(JobStage.ContentPending, jobs[1].Stage);
        Assert.Equal(1, jobs[1].GenerationRequestId);
        Assert.Equal(1, jobs[1].BaseStoryVersionId);
        Assert.Equal("retry-content-0001", jobs[1].OperationKey);
        Assert.Equal("pending_content", progress.CurrentStep);
        Assert.Equal("pending", progress.Content);
        Assert.Null(progress.LastErrorCode);
    }

    [Fact]
    public async Task Retry_content_generation_is_idempotent_for_same_retry_key()
    {
        var store = Seed();
        var failed = store.Items<StoryGenerationJob>().Single();
        failed.Status = GenerationJobStatus.Failed;
        failed.ErrorCode = "GEMINI_API_ERROR";
        var service = Service(store, new FakeAIClient());
        var request = new RetryContentGenerationRequestDto { RetryKey = "retry-content-0001" };

        await service.RetryAsync(1, 1, request);
        await service.RetryAsync(1, 1, request);

        Assert.Equal(2, store.Items<StoryGenerationJob>().Count);
        Assert.Single(store.Items<StoryGenerationJob>(), item =>
            item.Status == GenerationJobStatus.Pending && item.OperationKey == request.RetryKey);
    }

    [Fact]
    public async Task Retry_quality_failed_content_creates_new_candidate_without_reusing_failed_version()
    {
        var store = Seed();
        var failingService = new ContentGenerationService(
            store, new FakeAIClient(), new AlwaysFailReadabilityQualityEvaluator(),
            new FakeFailureFinalizer(store), new RecordingHandoffService(store),
            new ContentGenerationOptions { MaxContentRefinementAttempts = 2 });
        Assert.True(await failingService.ProcessNextAsync());
        var failed = store.Items<StoryGenerationJob>().Single();
        Assert.Equal(GenerationJobStatus.Failed, failed.Status);
        var failedVersionId = failed.StoryVersionId;
        Assert.NotNull(failedVersionId);

        var service = Service(store, new FakeAIClient());
        var request = new RetryContentGenerationRequestDto { RetryKey = "retry-quality-0001" };
        await service.RetryAsync(1, 1, request);
        await service.RetryAsync(1, 1, request);
        var retry = Assert.Single(store.Items<StoryGenerationJob>(), item => item.OperationKey == request.RetryKey);
        Assert.Null(retry.StoryVersionId);
        Assert.Equal(1, retry.BaseStoryVersionId);
        Assert.Equal(failedVersionId, failed.StoryVersionId);

        Assert.True(await service.ProcessNextAsync());
        Assert.Equal(GenerationJobStatus.Completed, retry.Status);
        Assert.NotNull(retry.StoryVersionId);
        Assert.NotEqual(failedVersionId, retry.StoryVersionId);
        Assert.Equal(GenerationJobStatus.Failed, failed.Status);
        Assert.Equal(GenerationJobOperation.GenerateVocabulary, PendingJob(store).Operation);
    }

    [Fact]
    public async Task Discussion_generation_automatically_marks_first_item_as_moral_lesson()
    {
        var store = Seed();
        var ai = new FakeAIClient();
        var service = Service(store, ai);

        Assert.True(await service.ProcessNextAsync()); // Content
        Assert.True(await service.ProcessNextAsync()); // Vocabulary
        Assert.True(await service.ProcessNextAsync()); // Quiz
        Assert.True(await service.ProcessNextAsync()); // Discussion

        var questions = store.Items<DiscussionQuestion>().ToList();
        Assert.NotEmpty(questions);
        Assert.True(questions[0].IsMoralLesson);
    }

    [Fact]
    public async Task Quiz_true_false_vietnamese_answers_are_normalized_to_boolean_strings()
    {
        var store = Seed();
        var ai = new VietnameseTrueFalseAIClient();
        var service = Service(store, ai);

        Assert.True(await service.ProcessNextAsync()); // Content
        Assert.True(await service.ProcessNextAsync()); // Vocabulary
        Assert.True(await service.ProcessNextAsync()); // Quiz

        var quizzes = store.Items<QuizItem>().Where(q => q.Type == QuizType.TrueFalse).ToList();
        Assert.Single(quizzes);
        Assert.Equal("true", quizzes[0].CorrectAnswer);
    }

    private static StoryGenerationJob PendingJob(FakeUnitOfWork store) =>
        store.Items<StoryGenerationJob>().Single(item => item.Status == GenerationJobStatus.Pending);

    private sealed class GrayTestProvider : StoryPlatform.Application.Features.Moderation.IModelModerationProvider
    {
        public string Name => "test-only";
        public Task<StoryPlatform.Application.Features.Moderation.ModelModerationResult> EvaluateAsync(
            StoryPlatform.Application.Features.Moderation.GrayZoneScan scan, CancellationToken ct)
        {
            var target = scan.Parts.First(x => x.Kind == "paragraph");
            return Task.FromResult(new StoryPlatform.Application.Features.Moderation.ModelModerationResult(50, "test-model",
                [new(target.Kind, target.Name, "context", "Needs context review")])
            { RubricVersion = scan.RubricVersion });
        }
    }

    [Fact]
    public async Task GrayZone_ParentPausesAndClearResumesSameCandidateWithoutRegenerating()
    {
        var store = Seed(); var ai = new FakeAIClient(); var handoff = new RecordingHandoffService(store);
        var evaluator = new StoryPlatform.Application.Features.Moderation.GrayZoneEvaluator(new()
        { Provider = "test-only", WarningThreshold = 30, BlockThreshold = 70, MaximumRiskScore = 100 }, [new GrayTestProvider()], Features.Moderation.RubricTestData.Source());
        var workflow = new StoryPlatform.Application.Features.Moderation.Services.GrayZoneWorkflowService(store,
            new Moq.Mock<StoryPlatform.Application.Features.AuditLogs.Interfaces.IAuditLogWriter>().Object);
        var service = new ContentGenerationService(store, ai, new PassingQualityEvaluator(), new FakeFailureFinalizer(store),
            handoff, new(), moderation: evaluator, grayZone: workflow);
        Assert.True(await service.ProcessNextAsync());
        var job = store.Items<StoryGenerationJob>().Single();
        Assert.Equal(GenerationJobStatus.WaitingForModeration, job.Status);
        Assert.Empty(handoff.Calls);
        Assert.False(await service.ProcessNextAsync());
        var candidateId = job.StoryVersionId;
        var item = store.Items<ModerationItem>().Single();
        store.Seed(new UserAccount { Id = 9, Role = UserRole.Moderator, Status = AccountStatus.Active });
        item.Status = ModerationItemStatus.Claimed; item.ClaimedByUserId = 9; item.ClaimedAt = DateTime.UtcNow;
        await workflow.DecideAsync(9, item.Id, new("Clear", "Safe in context", item.RowVersion), default);
        Assert.True(await service.ProcessNextAsync());
        Assert.Equal(GenerationJobStatus.Completed, job.Status);
        Assert.Equal(candidateId, store.Items<StoryVersion>().Single(x => x.IsCurrent).Id);
        Assert.Single(ai.Calls, x => x == "content");
        Assert.Single(handoff.Calls);
    }

    [Fact]
    public async Task GrayZone_RefinesQualityBeforeQueueingOnlyFinalCandidate()
    {
        var store = Seed(); var ai = new FakeAIClient();
        var evaluator = new StoryPlatform.Application.Features.Moderation.GrayZoneEvaluator(new()
        { Provider = "test-only", WarningThreshold = 30, BlockThreshold = 70, MaximumRiskScore = 100 }, [new GrayTestProvider()], Features.Moderation.RubricTestData.Source());
        var workflow = new StoryPlatform.Application.Features.Moderation.Services.GrayZoneWorkflowService(store,
            new Moq.Mock<StoryPlatform.Application.Features.AuditLogs.Interfaces.IAuditLogWriter>().Object);
        var service = new ContentGenerationService(store, ai, new FailOnceQualityEvaluator(), new FakeFailureFinalizer(store),
            new RecordingHandoffService(store), new(), moderation: evaluator, grayZone: workflow);
        await service.ProcessNextAsync();
        Assert.Contains("refine", ai.Calls);
        var item = Assert.Single(store.Items<ModerationItem>());
        Assert.Equal(store.Items<StoryVersion>().Max(x => x.Id), item.StoryVersionId);
        Assert.Equal(GenerationJobStatus.WaitingForModeration, store.Items<StoryGenerationJob>().Single().Status);
    }

    private static ContentGenerationService Service(FakeUnitOfWork store, IAIStoryGenerationClient ai) =>
        new(store, ai, new PassingQualityEvaluator(), new FakeFailureFinalizer(store), new RecordingHandoffService(store), new ContentGenerationOptions());

    private static FakeUnitOfWork Seed(decimal? safetyScoreThreshold = null)
    {
        var store = new FakeUnitOfWork();
        store.Seed(new Story { Id = 1, AuthorUserId = 1, ChildProfileId = 1, Source = StorySource.Ai, Status = StoryStatus.OutlineReview });
        var outline = new StoryVersion
        {
            Id = 1,
            StoryId = 1,
            VersionNo = 1,
            EditType = VersionEditType.Initial,
            Title = "Tình bạn",
            OutlineOpening = "Lan gặp Minh",
            OutlineDevelopment = "Hai bạn giúp nhau",
            OutlineEnding = "Hai bạn biết chia sẻ",
            IsCurrent = true,
            OutlineApprovedByUserId = 1,
            OutlineApprovedAt = DateTime.UtcNow
        };
        store.Seed(outline);
        var input = new AcceptedAIStoryInputSnapshot("Tình bạn", null, "ai_suggested", [], "ai_suggested", null,
            "Biết chia sẻ", "level_2", "vi", 500);
        var context = new AIStoryInputContextSnapshot(1, "6-8", 2, "level_2", "vi", 700, "always_manual", [], [], [], [])
        {
            SafetyScoreThreshold = safetyScoreThreshold,
            ConsentRecordedAt = DateTime.UtcNow,
            ConsentPolicyVersion = 1
        };
        store.Seed(new StoryGenerationRequest
        {
            Id = 1,
            StoryId = 1,
            SubmittedByUserId = 1,
            IdempotencyKey = "input-key",
            Status = GenerationInputStatus.InputAccepted,
            AcceptedInputJson = JsonSerializer.Serialize(input, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            ContextSnapshotJson = JsonSerializer.Serialize(context, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        });
        store.Seed(new StoryGenerationJob
        {
            Id = 1,
            StoryId = 1,
            GenerationRequestId = 1,
            BaseStoryVersionId = 1,
            RequestedByUserId = 1,
            OperationKey = "approval-key",
            Operation = GenerationJobOperation.GenerateContent,
            Stage = JobStage.ContentPending,
            Status = GenerationJobStatus.Pending,
            MaxAttempts = 3
        });
        return store;
    }

    private sealed class PassingQualityEvaluator : IContentQualityEvaluator
    {
        public ContentQualityResult Evaluate(StoryContentDto story, StoryOutlineDto approvedOutline,
            AcceptedAIStoryInputSnapshot input, AIStoryInputContextSnapshot context)
        {
            var pass = new ContentQualityGate(true, false, null, []);
            return new ContentQualityResult(true, pass, pass, pass, pass, pass);
        }
    }

    private sealed class FailOnceQualityEvaluator : IContentQualityEvaluator
    {
        private int _calls;
        public ContentQualityResult Evaluate(StoryContentDto story, StoryOutlineDto approvedOutline,
            AcceptedAIStoryInputSnapshot input, AIStoryInputContextSnapshot context)
        {
            _calls++;
            var pass = new ContentQualityGate(true, false, null, []);
            if (_calls > 1) return new ContentQualityResult(true, pass, pass, pass, pass, pass);
            var fail = new ContentQualityGate(false, true, "CONTENT_READABILITY_NOT_MET", ["Câu quá dài."]);
            return new ContentQualityResult(false, pass, pass, pass, fail, pass);
        }
    }

    private sealed class AlwaysFailReadabilityQualityEvaluator : IContentQualityEvaluator
    {
        public ContentQualityResult Evaluate(
            StoryContentDto story,
            StoryOutlineDto approvedOutline,
            AcceptedAIStoryInputSnapshot input,
            AIStoryInputContextSnapshot context)
        {
            var pass = new ContentQualityGate(true, false, null, []);
            var fail = new ContentQualityGate(
                false,
                true,
                "CONTENT_READABILITY_NOT_MET",
                ["Câu quá dài."]);
            return new ContentQualityResult(false, pass, pass, pass, fail, pass, 98m);
        }
    }

    private class FakeAIClient(
        bool invalidFirstVocabulary = false,
        bool blockSafety = false,
        double? safetyScore = null) : IAIStoryGenerationClient
    {
        private int _vocabularyCalls;
        public List<string> Calls { get; } = [];
        public Task<GenerateStoryContentResponse> GenerateStoryContentAsync(GenerateStoryContentRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add("content");
            return Task.FromResult(new GenerateStoryContentResponse
            {
                RequestId = request.RequestId,
                Story = new StoryContentDto
                {
                    Title = "Tình bạn",
                    Description = "Một câu chuyện về tình bạn và sự sẻ chia.",
                    Lesson = "Biết chia sẻ",
                    StorySections = [new StorySectionDto(1, "Câu chuyện", "Lan và Minh cùng chia sẻ một quyển sách.")]
                }
            });
        }
        public Task<GenerateVocabularyResponse> GenerateVocabularyAsync(GenerateVocabularyRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add("vocabulary");
            _vocabularyCalls++;
            return Task.FromResult(new GenerateVocabularyResponse
            {
                RequestId = request.RequestId,
                Items = invalidFirstVocabulary && _vocabularyCalls == 1
                    ? [new GeneratedVocabularyItemDto("không-có-trong-truyện", "Sai")]
                    : [new GeneratedVocabularyItemDto("chia sẻ", "Cùng dùng với người khác")]
            });
        }
        public virtual Task<GenerateQuizResponse> GenerateQuizAsync(GenerateQuizRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add("quiz");
            return Task.FromResult(new GenerateQuizResponse
            {
                RequestId = request.RequestId,
                Items =
                [
                    new QuizItemDto { Type = "multiple_choice", Question = "Ai chia sẻ sách?", Options = ["Lan", "Nam"], CorrectOptionIndex = 0, CorrectAnswer = "Lan" },
                    new QuizItemDto { Type = "true_false", Question = "Hai bạn cùng đọc sách?", CorrectAnswer = "true", CorrectOptionIndex = -1 },
                    new QuizItemDto { Type = "short_answer", Question = "Bài học là gì?", CorrectAnswer = "Biết chia sẻ", CorrectOptionIndex = -1 }
                ]
            });
        }
        public Task<GenerateDiscussionResponse> GenerateDiscussionAsync(GenerateDiscussionRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add("discussion");
            return Task.FromResult(new GenerateDiscussionResponse
            {
                RequestId = request.RequestId,
                Items = [new DiscussionQuestionDto("Em đã từng chia sẻ điều gì?")]
            });
        }
        public Task<EvaluateContentSafetyResponse> EvaluateContentSafetyAsync(EvaluateContentSafetyRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add("safety");
            return Task.FromResult(new EvaluateContentSafetyResponse
            {
                RequestId = request.RequestId,
                IsAllowed = !blockSafety,
                SafetyScore = safetyScore,
                CanRefine = false,
                ReasonCode = blockSafety ? "CONTENT_SAFETY_BLOCKED" : "CONTENT_SAFETY_ALLOWED"
            });
        }
        public Task<RefineStoryContentResponse> RefineStoryContentAsync(RefineStoryContentRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add("refine");
            return Task.FromResult(new RefineStoryContentResponse
            {
                RequestId = request.RequestId,
                Story = request.Story with
                {
                    StorySections = [new StorySectionDto(1, "Câu chuyện", "Lan và Minh cùng chia sẻ sách.")]
                }
            });
        }
        public Task<GenerateOutlineResponse> GenerateOutlineAsync(GenerateOutlineRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GenerateStoryResponse> GenerateStoryAsync(GenerateStoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RefineStoryResponse> RefineStoryAsync(RefineStoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EvaluateStoryResponse> EvaluateStoryAsync(EvaluateStoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class VietnameseTrueFalseAIClient : FakeAIClient
    {
        public override Task<GenerateQuizResponse> GenerateQuizAsync(GenerateQuizRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add("quiz");
            return Task.FromResult(new GenerateQuizResponse
            {
                RequestId = request.RequestId,
                Items =
                [
                    new QuizItemDto { Type = "multiple_choice", Question = "Ai chia sẻ sách?", Options = ["Lan", "Nam"], CorrectOptionIndex = 0, CorrectAnswer = "Lan" },
                    new QuizItemDto { Type = "true_false", Question = "Hai bạn cùng đọc sách?", CorrectAnswer = "Đúng", CorrectOptionIndex = 0 },
                    new QuizItemDto { Type = "short_answer", Question = "Bài học là gì?", CorrectAnswer = "Biết chia sẻ", CorrectOptionIndex = -1 }
                ]
            });
        }
    }

    private sealed class FakeFailureFinalizer(FakeUnitOfWork store) : IContentGenerationJobFailureFinalizer
    {
        public Task MarkFailedAsync(int jobId, string expectedConcurrencyToken, string errorCode, CancellationToken cancellationToken = default)
        {
            var job = store.Items<StoryGenerationJob>().Single(item => item.Id == jobId);
            if (job.ConcurrencyToken == expectedConcurrencyToken)
            {
                job.Status = GenerationJobStatus.Failed;
                job.ErrorCode = errorCode;
                job.LeaseExpiresAt = null;
            }
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        private readonly Dictionary<Type, object> _repositories = [];
        public IGenericRepository<T> Repository<T>() where T : class =>
            (IGenericRepository<T>)(_repositories.TryGetValue(typeof(T), out var value) ? value : _repositories[typeof(T)] = new FakeRepository<T>());
        public void Seed<T>(T item) where T : class => ((FakeRepository<T>)Repository<T>()).Items.Add(item);
        public IReadOnlyList<T> Items<T>() where T : class => ((FakeRepository<T>)Repository<T>()).Items;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AcquireTransactionLockAsync(int resourceId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeRepository<T> : IGenericRepository<T> where T : class
    {
        public List<T> Items { get; } = [];
        public Task<T?> GetByIdAsync(object id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(item => item is BaseEntity entity && entity.Id == Convert.ToInt32(id)));
        public Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<T>>(Items);
        public Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate, string? includeProperties = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<T>>(Items.Where(predicate.Compile()).ToArray());
        public Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, string? includeProperties = null, CancellationToken cancellationToken = default) => Task.FromResult(Items.FirstOrDefault(predicate.Compile()));
        public Task<(IReadOnlyList<T> Items, int TotalCount)> GetPagedAsync(int pageIndex, int pageSize, Expression<Func<T, bool>>? filter = null, Func<IQueryable<T>, IOrderedQueryable<T>>? orderBy = null, string? includeProperties = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<T> AddAsync(T entity, CancellationToken cancellationToken = default)
        {
            if (entity is BaseEntity item && item.Id == 0) item.Id = Items.OfType<BaseEntity>().Select(value => value.Id).DefaultIfEmpty().Max() + 1;
            Items.Add(entity);
            return Task.FromResult(entity);
        }
        public async Task AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default) { foreach (var item in entities) await AddAsync(item, cancellationToken); }
        public void Update(T entity) { }
        public void Delete(T entity) => Items.Remove(entity);
        public void DeleteRange(IEnumerable<T> entities) { foreach (var item in entities.ToArray()) Items.Remove(item); }
        public Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default) => Task.FromResult(predicate is null ? Items.Count : Items.Count(predicate.Compile()));
        public Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) => Task.FromResult(Items.Any(predicate.Compile()));
        public IQueryable<T> Query() => Items.AsQueryable();
    }

    /// <summary>
    /// Stub cho IStableVersionArtifactHandoffService - giả lập việc tạo job Vocabulary.
    /// Đảm bảo idempotency và khớp với behavior thật của handoff service.
    /// </summary>
    private sealed class RecordingHandoffService(FakeUnitOfWork store) : IStableVersionArtifactHandoffService
    {
        public List<(int StoryId, int VersionId, int? GenerationRequestId)> Calls { get; } = [];

        public async Task<StableVersionHandoffResult> QueueArtifactsAsync(
            int storyId, int storyVersionId, int requestedByUserId,
            int? generationRequestId, CancellationToken cancellationToken = default)
        {
            Calls.Add((storyId, storyVersionId, generationRequestId));

            // Idempotent: nếu đã có job cho (story, version, GenerateVocabulary), trả về job đó.
            var existing = store.Items<StoryGenerationJob>().FirstOrDefault(job =>
                job.StoryId == storyId
                && job.Operation == GenerationJobOperation.GenerateVocabulary
                && job.StoryVersionId == storyVersionId);
            if (existing is not null) return new StableVersionHandoffResult(true, existing.Id);

            // Tạo job mới tương tự StableVersionArtifactHandoffService.
            var jobEntity = new StoryGenerationJob
            {
                StoryId = storyId,
                GenerationRequestId = generationRequestId,
                StoryVersionId = storyVersionId,
                BaseStoryVersionId = storyVersionId,
                RequestedByUserId = requestedByUserId,
                OperationKey = $"existing:p3:{storyId}:v{storyVersionId}:op4",
                Operation = GenerationJobOperation.GenerateVocabulary,
                Stage = JobStage.ContentArtifactPending,
                Status = GenerationJobStatus.Pending,
                AttemptNo = 0,
                MaxAttempts = 3,
                StartedAt = DateTime.UtcNow
            };
            await store.Repository<StoryGenerationJob>().AddAsync(jobEntity, cancellationToken);
            return new StableVersionHandoffResult(true, jobEntity.Id);
        }
    }
}
