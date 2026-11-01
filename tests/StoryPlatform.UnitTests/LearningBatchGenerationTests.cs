using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StoryPlatform.Application.Abstractions.AI;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.AIStoryInput.Models;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Application.Features.ContentGeneration;
using StoryPlatform.Application.Features.ContentGeneration.Interfaces;
using StoryPlatform.Application.Features.ContentGeneration.DTOs;
using StoryPlatform.Application.Features.ContentGeneration.Quality;
using StoryPlatform.Application.Features.ContentGeneration.Services;
using StoryPlatform.Application.Features.ExistingStories.Interfaces;
using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Contracts.AI.Responses;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.AI;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class LearningBatchGenerationTests
{
    [Fact]
    public async Task Retry_latest_coordinator_failure_preserves_new_valid_quizzes_instead_of_replaying_old_quiz_failure()
    {
        var s=Seed();s.Root.Status=GenerationJobStatus.Pending;var client=Client();
        using var provider=Provider(s.Store,client.Object);using var scope=provider.CreateScope();
        var finalizer=new Mock<IContentGenerationJobFailureFinalizer>();
        var service=new ContentGenerationService(s.Store,client.Object,new Mock<IContentQualityEvaluator>().Object,
            finalizer.Object,new Mock<IStableVersionArtifactHandoffService>().Object,new ContentGenerationOptions(),
            learningBatches:scope.ServiceProvider.GetRequiredService<ILearningBatchService>());
        Assert.True(await service.ProcessNextAsync());
        var quiz=s.Store.Items<StoryGenerationJob>().Single(x=>x.Operation==GenerationJobOperation.GenerateQuiz);
        quiz.Status=GenerationJobStatus.Failed;quiz.ErrorCode="QUIZ_VALIDATION_FAILED";quiz.CompletedAt=DateTime.UtcNow.AddHours(-1);
        s.Root.Status=GenerationJobStatus.Failed;s.Root.ErrorCode="VOCABULARY_GENERATION_FAILED";s.Root.CompletedAt=DateTime.UtcNow;
        var saved=s.Store.Items<LearningArtifactGenerationBatch>().ToDictionary(x=>x.Id,x=>x.ResultJson);
        var progress=await service.GetProgressAsync(1,1);
        Assert.Equal("VOCABULARY_GENERATION_FAILED",progress.LastErrorCode);
        await service.RetryAsync(1,1,new() {RetryKey="resume-cached-coordinator"});
        Assert.Equal(GenerationJobStatus.Pending,s.Root.Status);
        Assert.All(s.Store.Items<LearningArtifactGenerationBatch>(),x=>{Assert.Equal(GenerationJobStatus.Completed,x.Status);Assert.Equal(saved[x.Id],x.ResultJson);});
        for(var i=0;i<2;i++)Assert.True(await service.ProcessNextAsync());
        Assert.Equal(StoryStatus.ContentReview,s.Story.Status);Assert.Equal(24,s.Store.Items<QuizItem>().Count);
        client.Verify(x=>x.GenerateVocabularyAsync(It.IsAny<GenerateVocabularyRequest>(),It.IsAny<CancellationToken>()),Times.Once);
        client.Verify(x=>x.GenerateQuizAsync(It.IsAny<GenerateQuizRequest>(),It.IsAny<CancellationToken>()),Times.Exactly(3));
    }
    [Theory]
    [InlineData(true,"QUIZ_TYPE_COVERAGE_INVALID")]
    [InlineData(false,"QUIZ_QUESTION_RELEVANCE_INVALID")]
    public async Task FinalQuizRulesAreRetriedInsideOnlyTheInvalidBatch(bool missingTypes,string expectedCode)
    {
        var s=Seed();var calls=0;
        var client=Client(transform:(request,response)=>
        {
            if(request.Batch!.Kind!="quiz_facts")return response;
            if(Interlocked.Increment(ref calls)>1){Assert.Equal(expectedCode,request.Batch.ValidationErrorCode);return response;}
            return response with {Items=response.Items.Select(item=>missingTypes?item with
            {
                Type="multiple_choice",Options=["Na","Nam"],CorrectAnswer="Na",CorrectOptionIndex=0,
                OptionFeedback=["Na là nhân vật được hỏi.","Hãy xem lại việc Na làm."]
            }:item with {Question="Unrelated astronomy question?"}).ToArray()};
        });
        using var provider=Provider(s.Store,client.Object);using var scope=provider.CreateScope();
        var result=await scope.ServiceProvider.GetRequiredService<ILearningBatchService>()
            .GenerateAsync(s.Root,s.Root.ConcurrencyToken,s.Version,s.Context,default);
        Assert.Equal(24,result.Quiz.Items.Count);
        Assert.Equal(3,result.Quiz.Items.Select(x=>x.Type).Distinct().Count());
        Assert.Equal(2,s.Store.Items<LearningArtifactGenerationBatch>().Single(x=>x.Kind=="quiz_facts").AttemptCount);
        Assert.All(s.Store.Items<LearningArtifactGenerationBatch>().Where(x=>x.Kind!="quiz_facts"),x=>Assert.Equal(1,x.AttemptCount));
        client.Verify(x=>x.GenerateQuizAsync(It.IsAny<GenerateQuizRequest>(),It.IsAny<CancellationToken>()),Times.Exactly(4));
    }
    private const string Text = "Na kiên nhẫn sửa xe. Nam chờ bạn giúp đỡ tìm bánh. Lan mang hộp đồ nhỏ xanh đẹp.";
    private sealed class Permit : IDisposable { public void Dispose() { } }
    private sealed class Gate : ILearningRequestGate { public Task<IDisposable> EnterAsync(CancellationToken ct) => Task.FromResult<IDisposable>(new Permit()); }

    private static (StoryReviewServiceTests.FakeUnitOfWork Store, Story Story, StoryVersion Version, StoryGenerationJob Root, AIStoryInputContextSnapshot Context) Seed(string age = "6-8")
    {
        var store = new StoryReviewServiceTests.FakeUnitOfWork();
        store.Seed(new UserAccount { Id = 1, Role = UserRole.ContentCreator, Status = AccountStatus.Active });
        var story = new Story { Id = 1, AuthorUserId = 1, Scope = StoryScope.Creator, Source = StorySource.Ai,
            AgeBand = age, ReadingLevel = 2, Language = "vi", OutputMode = StoryOutputMode.Learning, Status = StoryStatus.OutlineReview };
        var version = new StoryVersion { Id = 10, StoryId = 1, IsCurrent = true, Title = "Na sửa xe", Content = Text, Lesson = "Kiên nhẫn" };
        var root = new StoryGenerationJob { Id = 20, StoryId = 1, StoryVersionId = 10, GenerationRequestId = 1, RequestedByUserId = 1,
            Operation = GenerationJobOperation.GenerateVocabulary, Status = GenerationJobStatus.Processing,
            LeaseExpiresAt = DateTime.UtcNow.AddMinutes(10), ConcurrencyToken = "root-claim" };
        var context = new AIStoryInputContextSnapshot(null, age, 2, "level_2", "vi", 1800, "always_manual", [], [], [], [],
            AudienceScope: "Creator", CreatorUserId: 1) { LearningProfile = LearningGenerationProfile.ForAge(age) };
        store.Seed(story); store.Seed(version); store.Seed(root);
        store.Seed(new StoryGenerationRequest { Id = 1, StoryId = 1, SubmittedByUserId = 1, Status = GenerationInputStatus.InputAccepted,
            ContextSnapshotJson = JsonSerializer.Serialize(context, new JsonSerializerOptions(JsonSerializerDefaults.Web)) });
        store.Seed(new PromptCatalogVersion { Id = 2, GradeBand = age, VersionNo = "v1", Status = CatalogStatus.Published });
        return (store, story, version, root, context);
    }

    private static Mock<IAIStoryGenerationClient> Client(Action<GenerateQuizRequest>? beforeQuiz = null,
        Func<GenerateQuizRequest, GenerateQuizResponse, GenerateQuizResponse>? transform = null)
    {
        var ai = new Mock<IAIStoryGenerationClient>();
        ai.Setup(x => x.GenerateVocabularyAsync(It.IsAny<GenerateVocabularyRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GenerateVocabularyRequest r, CancellationToken _) => new GenerateVocabularyResponse
            {
                Items = Text.Split([' ', '.'], StringSplitOptions.RemoveEmptyEntries).Distinct().Take(r.Batch!.TargetCount)
                    .Select(term => new GeneratedVocabularyItemDto(term, "Nghĩa phù hợp truyện") { SourceQuote = Text, Example = Text }).ToArray()
            });
        ai.Setup(x => x.GenerateQuizAsync(It.IsAny<GenerateQuizRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GenerateQuizRequest r, CancellationToken _) =>
            {
                beforeQuiz?.Invoke(r);
                var response = new GenerateQuizResponse { Items = Enumerable.Range(0, r.Batch!.TargetCount).Select(i => new StoryPlatform.Contracts.AI.Models.QuizItemDto
                {
                    Type = i % 3 == 0 ? "multiple_choice" : i % 3 == 1 ? "true_false" : "short_answer",
                    Question = $"Na kiên nhẫn {r.Batch.Kind} {i} làm gì?", SourceQuote = Text,
                    LearningGoal = r.Batch.Kind == "quiz_vocabulary" ? "vocabulary" : r.Batch.Kind == "quiz_reasoning" ? "inference" : "recall",
                    Options = i % 3 == 0 ? ["Na", "Nam"] : [], CorrectOptionIndex = 0,
                    CorrectAnswer = i % 3 == 1 ? "true" : "Na", Explanation = "Na kiên nhẫn sửa xe.", Hint = "Hãy đọc lại đoạn đầu.",
                    OptionFeedback = i % 3 == 0 ? ["Na là nhân vật được hỏi.", "Hãy xem lại việc Na làm."] : []
                }).ToArray() };
                return transform?.Invoke(r, response) ?? response;
            });
        return ai;
    }
    private static ServiceProvider Provider(IUnitOfWork store, IAIStoryGenerationClient ai)
    {
        var services = new ServiceCollection();
        services.AddLogging(); services.AddSingleton(store); services.AddSingleton(ai);
        services.AddSingleton(new Mock<IChildProfileAccessGuard>().Object);
        services.AddSingleton(new LearningBatchOptions { BaseRetryDelaySeconds = 0, QuotaRetryFloorSeconds = 0 });
        services.AddSingleton<ILearningRequestGate, Gate>();
        services.AddSingleton<ILearningBatchRunner, LearningBatchRunner>();
        services.AddScoped<ILearningBatchProcessor, LearningBatchProcessor>();
        services.AddScoped<ILearningBatchService, LearningBatchService>();
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData("6-8", 12, 24)]
    [InlineData("9-10", 18, 36)]
    public async Task GeneratesGroundedPackageForBothAgesAndReplaysWithoutMoreProviderCalls(string age, int vocabulary, int quiz)
    {
        var s = Seed(age);
        var client = Client(r =>
        {
            if (r.Batch!.Kind == "quiz_vocabulary") Assert.NotEmpty(r.Vocabulary);
            else Assert.Empty(r.Vocabulary);
        });
        using var provider = Provider(s.Store, client.Object);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ILearningBatchService>();
        var result = await service.GenerateAsync(s.Root, s.Root.ConcurrencyToken, s.Version, s.Context, default);
        Assert.Equal(vocabulary, result.Vocabulary.Items.Count);
        Assert.Equal(quiz, result.Quiz.Items.Count);
        Assert.All(s.Store.Items<LearningArtifactGenerationBatch>(), x => Assert.Equal(GenerationJobStatus.Completed, x.Status));
        await service.GenerateAsync(s.Root, s.Root.ConcurrencyToken, s.Version, s.Context, default);
        Assert.Equal(4, s.Store.Items<LearningArtifactGenerationBatch>().Count);
        client.Verify(x => x.GenerateVocabularyAsync(It.IsAny<GenerateVocabularyRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(x => x.GenerateQuizAsync(It.IsAny<GenerateQuizRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task RetryOnlyCallsTheFailedBatchAndKeepsSuccessfulResults()
    {
        var s = Seed(); var failures = 0;
        var client = Client(r => { if (r.Batch!.Kind == "quiz_facts" && Interlocked.Increment(ref failures) == 1)
            throw new AIServiceRequestException(429, "AI_PROVIDER_429", "rate limited", TimeSpan.Zero); });
        using var provider = Provider(s.Store, client.Object); using var scope = provider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ILearningBatchService>().GenerateAsync(s.Root, s.Root.ConcurrencyToken, s.Version, s.Context, default);
        Assert.Equal(24, result.Quiz.Items.Count);
        Assert.Equal(2, s.Store.Items<LearningArtifactGenerationBatch>().Single(x => x.Kind == "quiz_facts").AttemptCount);
        Assert.All(s.Store.Items<LearningArtifactGenerationBatch>().Where(x => x.Kind != "quiz_facts"), x => Assert.Equal(1, x.AttemptCount));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Output_validation_retry_receives_feedback_and_keeps_successful_batches(bool coreValidation)
    {
        var s = Seed(); var calls = 0;
        var client = Client(request =>
        {
            if (request.Batch!.Kind != "quiz_facts") return;
            if (Interlocked.Increment(ref calls) == 1)
            {
                Assert.Null(request.Batch.ValidationErrorCode);
                if (coreValidation) throw new JsonException("QUIZ_OPTIONS_INVALID");
                throw new AIServiceRequestException(502, "AI_OUTPUT_INVALID", "Output invalid", validationCode: "QUIZ_OPTIONS_INVALID");
            }
            Assert.Equal("QUIZ_OPTIONS_INVALID", request.Batch.ValidationErrorCode);
        });
        using var provider = Provider(s.Store, client.Object); using var scope = provider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ILearningBatchService>()
            .GenerateAsync(s.Root, s.Root.ConcurrencyToken, s.Version, s.Context, default);
        Assert.Equal(24, result.Quiz.Items.Count);
        var facts = s.Store.Items<LearningArtifactGenerationBatch>().Single(x => x.Kind == "quiz_facts");
        Assert.Equal(2, facts.AttemptCount);
        Assert.DoesNotContain("validationErrorCode", facts.RequestJson);
        Assert.All(s.Store.Items<LearningArtifactGenerationBatch>().Where(x => x.Id != facts.Id), x => Assert.Equal(1, x.AttemptCount));
    }

    [Fact]
    public async Task Invalid_output_stops_after_three_attempts_and_retains_diagnostic_code()
    {
        var s = Seed();
        var client = Client(request =>
        {
            if (request.Batch!.Kind == "quiz_facts")
                throw new AIServiceRequestException(502, "AI_OUTPUT_INVALID", "Output invalid", validationCode: "LEARNING_EVIDENCE_NOT_IN_STORY");
        });
        using var provider = Provider(s.Store, client.Object); using var scope = provider.CreateScope();
        await Assert.ThrowsAnyAsync<Exception>(() => scope.ServiceProvider.GetRequiredService<ILearningBatchService>()
            .GenerateAsync(s.Root, s.Root.ConcurrencyToken, s.Version, s.Context, default));
        var facts = s.Store.Items<LearningArtifactGenerationBatch>().Single(x => x.Kind == "quiz_facts");
        Assert.Equal(3, facts.AttemptCount);
        Assert.Equal(GenerationJobStatus.Failed, facts.Status);
        Assert.Equal("LEARNING_EVIDENCE_NOT_IN_STORY", facts.ErrorCode);
        Assert.Contains(s.Store.Items<LearningArtifactGenerationBatch>(), x => x.Kind == "vocabulary" && x.Status == GenerationJobStatus.Completed);
    }

    [Fact]
    public async Task TruncatedOutputFailsImmediatelyInsteadOfBurningTheAttemptBudget()
    {
        var s = Seed(); var calls = 0;
        var client = Client(request =>
        {
            if (request.Batch!.Kind != "quiz_facts") return;
            Interlocked.Increment(ref calls);
            throw new AIServiceRequestException(502, "AI_OUTPUT_TRUNCATED", "Output truncated");
        });
        using var provider = Provider(s.Store, client.Object); using var scope = provider.CreateScope();
        await Assert.ThrowsAnyAsync<Exception>(() => scope.ServiceProvider.GetRequiredService<ILearningBatchService>()
            .GenerateAsync(s.Root, s.Root.ConcurrencyToken, s.Version, s.Context, default));
        var facts = s.Store.Items<LearningArtifactGenerationBatch>().Single(x => x.Kind == "quiz_facts");
        // The same ceiling truncates again, so one call is the honest cost; retries would only re-burn quota.
        Assert.Equal(1, calls);
        Assert.Equal(1, facts.AttemptCount);
        Assert.Equal(GenerationJobStatus.Failed, facts.Status);
        Assert.Equal("AI_OUTPUT_TRUNCATED", facts.ErrorCode);
    }

    [Fact]
    public void QuotaRetryFloorDefaultsToAWholeQuotaWindow()
    {
        // Retrying a rate limit after seconds lands in the same TPM window; the default must span it.
        Assert.Equal(60, new LearningBatchOptions().QuotaRetryFloorSeconds);
        // Zero disables the floor, which is what tests and single-shot callers want.
        Assert.Equal(0, new LearningBatchOptions { QuotaRetryFloorSeconds = 0 }.QuotaRetryFloorSeconds);
    }

    [Fact]
    public async Task Explicit_retry_retains_validation_feedback_after_automatic_attempts_are_exhausted()
    {
        var s = Seed(); s.Root.Status = GenerationJobStatus.Pending;
        var invalid = true;
        var client = Client(request =>
        {
            if (request.Batch!.Kind != "quiz_facts") return;
            if (invalid) throw new AIServiceRequestException(502, "AI_OUTPUT_INVALID", "Output invalid", validationCode: "QUIZ_OPTIONS_INVALID");
            Assert.Equal("QUIZ_OPTIONS_INVALID", request.Batch.ValidationErrorCode);
        });
        using var provider = Provider(s.Store, client.Object); using var scope = provider.CreateScope();
        var finalizer = new Mock<IContentGenerationJobFailureFinalizer>();
        finalizer.Setup(x => x.MarkFailedAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((int id, string token, string code, CancellationToken ct) =>
            { var job = s.Store.Items<StoryGenerationJob>().Single(x => x.Id == id); job.ErrorCode = code; job.Status = GenerationJobStatus.Failed; return Task.CompletedTask; });
        var service = new ContentGenerationService(s.Store, client.Object, new Mock<IContentQualityEvaluator>().Object,
            finalizer.Object, new Mock<IStableVersionArtifactHandoffService>().Object, new ContentGenerationOptions(),
            learningBatches: scope.ServiceProvider.GetRequiredService<ILearningBatchService>());
        Assert.True(await service.ProcessNextAsync());
        Assert.Equal(GenerationJobStatus.Failed, s.Root.Status);
        var facts = s.Store.Items<LearningArtifactGenerationBatch>().Single(x => x.Kind == "quiz_facts");
        Assert.Equal(3, facts.AttemptCount);
        invalid = false;
        await service.RetryAsync(1, 1, new() { RetryKey = "repair-feedback-after-exhaustion" });
        Assert.Equal("QUIZ_OPTIONS_INVALID", facts.ErrorCode);
        for (var i = 0; i < 2; i++) Assert.True(await service.ProcessNextAsync());
        Assert.Equal(StoryStatus.ContentReview, s.Story.Status);
        Assert.Equal(24, s.Store.Items<QuizItem>().Count);
        client.Verify(x => x.GenerateVocabularyAsync(It.IsAny<GenerateVocabularyRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WholeArtifactPipelinePersistsEvidenceHintsAndExplanationWithoutRegeneratingQuiz()
    {
        var s = Seed(); s.Root.Status = GenerationJobStatus.Pending;
        var client = Client();
        using var provider = Provider(s.Store, client.Object); using var scope = provider.CreateScope();
        var finalizer = new Mock<IContentGenerationJobFailureFinalizer>();
        finalizer.Setup(x => x.MarkFailedAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((int id, string token, string code, CancellationToken ct) =>
            { var job = s.Store.Items<StoryGenerationJob>().Single(x => x.Id == id); job.ErrorCode = code; job.Status = GenerationJobStatus.Failed; return Task.CompletedTask; });
        var service = new ContentGenerationService(s.Store, client.Object, new Mock<IContentQualityEvaluator>().Object,
            finalizer.Object, new Mock<IStableVersionArtifactHandoffService>().Object, new ContentGenerationOptions(),
            learningBatches: scope.ServiceProvider.GetRequiredService<ILearningBatchService>());
        for (var i = 0; i < 2; i++) Assert.True(await service.ProcessNextAsync());
        Assert.All(s.Store.Items<StoryGenerationJob>(), x => Assert.True(x.Status == GenerationJobStatus.Completed, x.ErrorCode));
        Assert.Equal(StoryStatus.ContentReview, s.Story.Status);
        Assert.Equal(12, s.Store.Items<StoryVocabulary>().Count);
        Assert.Equal(24, s.Store.Items<QuizItem>().Count);
        Assert.All(s.Store.Items<QuizItem>(), item =>
        {
            Assert.NotEmpty(item.Explanation!); Assert.NotEmpty(item.Hint!);
            Assert.Equal(item.SourceQuote, Text[item.SourceStart!.Value..item.SourceEnd!.Value]);
        });
        client.Verify(x => x.GenerateQuizAsync(It.IsAny<GenerateQuizRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RetryRebuildsQuizRejectedByFinalValidationWhilePreservingVocabulary(bool missingQuestionTypes)
    {
        var s = Seed(); s.Root.Status = GenerationJobStatus.Pending;
        var client = Client();
        using var provider = Provider(s.Store, client.Object); using var scope = provider.CreateScope();
        var finalizer = new Mock<IContentGenerationJobFailureFinalizer>();
        finalizer.Setup(x => x.MarkFailedAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((int id, string token, string code, CancellationToken ct) =>
            { var job = s.Store.Items<StoryGenerationJob>().Single(x => x.Id == id); job.ErrorCode = code; job.Status = GenerationJobStatus.Failed; return Task.CompletedTask; });
        var service = new ContentGenerationService(s.Store, client.Object, new Mock<IContentQualityEvaluator>().Object,
            finalizer.Object, new Mock<IStableVersionArtifactHandoffService>().Object, new ContentGenerationOptions(),
            learningBatches: scope.ServiceProvider.GetRequiredService<ILearningBatchService>());
        Assert.True(await service.ProcessNextAsync());
        var vocabulary = s.Store.Items<LearningArtifactGenerationBatch>().Single(x => x.Kind == "vocabulary");
        var vocabularyResult = vocabulary.ResultJson;
        Assert.All(s.Store.Items<LearningArtifactGenerationBatch>(), x => Assert.Equal(GenerationJobStatus.Completed, x.Status));
        // Simulate completed results cached by a release that did not enforce the final quiz rules per batch.
        foreach(var batch in s.Store.Items<LearningArtifactGenerationBatch>().Where(x=>x.Kind.StartsWith("quiz_")))
        {
            var response=JsonSerializer.Deserialize<GenerateQuizResponse>(batch.ResultJson!,new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            response=response with {Items=response.Items.Select((item,i)=>missingQuestionTypes?item with
            {
                Type="multiple_choice",Options=["Na","Nam"],CorrectAnswer="Na",CorrectOptionIndex=0,
                OptionFeedback=["Na là nhân vật được hỏi.","Hãy xem lại việc Na làm."]
            }:item with {Question=$"Unrelated astronomy {batch.Kind} {i}?"}).ToArray()};
            batch.ResultJson=JsonSerializer.Serialize(response,new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        Assert.True(await service.ProcessNextAsync());
        var quiz = s.Store.Items<StoryGenerationJob>().Single(x => x.Operation == GenerationJobOperation.GenerateQuiz);
        Assert.Equal("QUIZ_VALIDATION_FAILED", quiz.ErrorCode);
        Assert.Equal(GenerationJobStatus.Failed, quiz.Status);
        Assert.Empty(s.Store.Items<QuizItem>());

        var retry = new RetryContentGenerationRequestDto { RetryKey = "rebuild-invalid-quiz-001" };
        await service.RetryAsync(1, 1, retry);
        await service.RetryAsync(1, 1, retry); // replay must not duplicate or requeue more work
        Assert.Equal(GenerationJobStatus.Pending, s.Root.Status);
        Assert.Equal(GenerationJobStatus.Failed, quiz.Status); // wait for the coordinator
        Assert.Equal(GenerationJobStatus.Completed, vocabulary.Status);
        Assert.Equal(vocabularyResult, vocabulary.ResultJson);
        Assert.All(s.Store.Items<LearningArtifactGenerationBatch>().Where(x => x.Kind != "vocabulary"), x =>
        { Assert.Equal(GenerationJobStatus.Pending, x.Status); Assert.Null(x.ResultJson); });
        for (var i = 0; i < 2; i++) Assert.True(await service.ProcessNextAsync(),
            string.Join("; ", s.Store.Items<StoryGenerationJob>().Select(x => $"{x.Operation}:{x.Status}:{x.ErrorCode}")));
        Assert.All(s.Store.Items<StoryGenerationJob>(), x => Assert.Equal(GenerationJobStatus.Completed, x.Status));
        Assert.Equal(StoryStatus.ContentReview, s.Story.Status);
        Assert.Equal(12, s.Store.Items<StoryVocabulary>().Count);
        Assert.Equal(24, s.Store.Items<QuizItem>().Count);
        Assert.Equal(4, s.Store.Items<LearningArtifactGenerationBatch>().Count);
        client.Verify(x => x.GenerateVocabularyAsync(It.IsAny<GenerateVocabularyRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(x => x.GenerateQuizAsync(It.IsAny<GenerateQuizRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(6));
    }

    [Fact]
    public async Task ResultIsRejectedIfSourceOrActorChangesWhileProviderIsRunning()
    {
        var s = Seed();
        var client = Client(_ => s.Version.Content = "Một câu chuyện khác.");
        using var provider = Provider(s.Store, client.Object); using var scope = provider.CreateScope();
        await Assert.ThrowsAnyAsync<Exception>(() => scope.ServiceProvider.GetRequiredService<ILearningBatchService>()
            .GenerateAsync(s.Root, s.Root.ConcurrencyToken, s.Version, s.Context, default));
        Assert.DoesNotContain(s.Store.Items<LearningArtifactGenerationBatch>(), x => x.Kind.StartsWith("quiz_") && x.Status == GenerationJobStatus.Completed);
        Assert.Empty(s.Store.Items<QuizItem>());
    }
}
