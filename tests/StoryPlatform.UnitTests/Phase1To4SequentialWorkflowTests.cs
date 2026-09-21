using System.Linq.Expressions;
using StoryPlatform.Application.Abstractions.AI;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AIStoryInput.DTOs;
using StoryPlatform.Application.Features.AIStoryInput.Guardrails;
using StoryPlatform.Application.Features.AIStoryInput.Models;
using StoryPlatform.Application.Features.AIStoryInput.Services;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.ContentGeneration;
using StoryPlatform.Application.Features.ContentGeneration.Interfaces;
using StoryPlatform.Application.Features.ContentGeneration.Quality;
using StoryPlatform.Application.Features.ContentGeneration.Services;
using StoryPlatform.Application.Features.ExistingStories.Interfaces;
using StoryPlatform.Application.Features.Outline.DTOs;
using StoryPlatform.Application.Features.Outline.Guardrails;
using StoryPlatform.Application.Features.Outline.Interfaces;
using StoryPlatform.Application.Features.Outline.Services;
using StoryPlatform.Application.Features.StoryReview.DTOs;
using StoryPlatform.Application.Features.StoryReview.Services;
using StoryPlatform.Application.Features.TokenQuota.Interfaces;
using StoryPlatform.Application.Features.TokenQuota.Services;
using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Contracts.AI.Responses;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

using ReviewDiscussionItemDto = StoryPlatform.Application.Features.StoryReview.DTOs.DiscussionItemDto;
using ReviewQuizItemDto = StoryPlatform.Application.Features.StoryReview.DTOs.QuizItemDto;
using ReviewVocabularyItemDto = StoryPlatform.Application.Features.StoryReview.DTOs.VocabularyItemDto;

namespace StoryPlatform.UnitTests;

public sealed class Phase1To4SequentialWorkflowTests
{
    [Fact]
    public async Task Happy_path_runs_phase1_to_phase4_and_approves_story()
    {
        var store = CreateEligibleStore();
        var ai = new WorkflowAIClient();

        // Phase 1: resolve child profile/safety context and accept input.
        var inputService = new AIStoryInputService(store, new RuleBasedInputGuardrail(), CreateTokenQuotaService(store));
        var inputResult = await inputService.SubmitAsync(1, ValidInput());
        var story = Assert.Single(store.Items<Story>());
        Assert.Equal("input_accepted", inputResult.InputStatus);
        Assert.Equal(StoryStatus.Draft, story.Status);
        Assert.Single(store.Items<StoryGenerationJob>(), job => job.Operation == GenerationJobOperation.GenerateOutline);

        // Phase 2: generate and approve the outline, then hand off to content generation.
        var outlineService = new OutlineService(
            store,
            ai,
            new RuleBasedOutlineReviewGuardrail(),
            new OutlineFailureFinalizer(store));
        Assert.True(await outlineService.ProcessNextAsync());
        var outline = Assert.Single(store.Items<StoryVersion>());
        Assert.Equal(StoryStatus.OutlineReview, story.Status);
        Assert.True(outline.IsCurrent);
        Assert.Null(outline.Content);

        await outlineService.ApproveAsync(
            1,
            story.Id,
            outline.VersionNo,
            new ApproveOutlineRequestDto { ApprovalKey = "phase2-approval-0001" });
        Assert.NotNull(outline.OutlineApprovedAt);
        Assert.Single(store.Items<StoryGenerationJob>(), job =>
            job.Operation == GenerationJobOperation.GenerateContent && job.Status == GenerationJobStatus.Pending);

        // Phase 3: generate content, vocabulary, quiz and discussion sequentially.
        var artifactHandoff = new RecordingArtifactHandoff(store);
        var contentService = new ContentGenerationService(
            store,
            ai,
            new PassingContentQualityEvaluator(),
            new ContentFailureFinalizer(store),
            artifactHandoff,
            new ContentGenerationOptions());
        Assert.True(await contentService.ProcessNextAsync());
        Assert.Equal(
            store.Items<StoryGenerationRequest>().Single().Id,
            Assert.Single(artifactHandoff.Calls).GenerationRequestId);
        Assert.True(await contentService.ProcessNextAsync());
        Assert.True(await contentService.ProcessNextAsync());
        Assert.True(await contentService.ProcessNextAsync());

        var stableVersion = store.Items<StoryVersion>().Single(version => version.IsCurrent && version.Content is not null);
        Assert.Equal(StoryStatus.ContentReview, story.Status);
        Assert.Equal(
            ["outline", "content", "safety", "vocabulary", "quiz", "discussion"],
            ai.Calls);

        // Phase 4: raw Phase 3 output is not approvable until the human completes required artifacts.
        var reviewService = new StoryReviewService(store, ai, new InMemoryProposalCache());
        var beforeReview = await reviewService.ValidateAsync(1, story.Id);
        Assert.False(beforeReview.CanApprove);

        await reviewService.UpdateVocabularyAsync(1, story.Id, new UpdateVocabularyRequestDto
        {
            VersionId = stableVersion.Id,
            Items =
            [
                new ReviewVocabularyItemDto { Term = "Lan", Definition = "Tên một nhân vật" },
                new ReviewVocabularyItemDto { Term = "Minh", Definition = "Tên một nhân vật" },
                new ReviewVocabularyItemDto { Term = "chia sẻ", Definition = "Cùng dùng với người khác" },
                new ReviewVocabularyItemDto { Term = "rừng", Definition = "Nơi có nhiều cây" },
                new ReviewVocabularyItemDto { Term = "bạn bè", Definition = "Những người thân thiết" }
            ]
        });
        await reviewService.UpdateQuizAsync(1, story.Id, new UpdateQuizRequestDto
        {
            VersionId = stableVersion.Id,
            Items =
            [
                new ReviewQuizItemDto
                {
                    Type = "MultipleChoice", Question = "Ai chia sẻ sách?", CorrectAnswer = "Lan", Choices = ["Lan", "Nam"]
                },
                new ReviewQuizItemDto
                {
                    Type = "TrueFalse", Question = "Hai bạn cùng đọc sách?", CorrectAnswer = "true"
                },
                new ReviewQuizItemDto
                {
                    Type = "ShortAnswer", Question = "Bài học là gì?", CorrectAnswer = "Biết chia sẻ"
                }
            ]
        });
        await reviewService.UpdateDiscussionAsync(1, story.Id, new UpdateDiscussionRequestDto
        {
            VersionId = stableVersion.Id,
            Items =
            [
                new ReviewDiscussionItemDto { Question = "Em học được gì từ câu chuyện?", IsMoralLesson = true },
                new ReviewDiscussionItemDto { Question = "Em sẽ chia sẻ điều gì với bạn?" }
            ]
        });

        var afterReview = await reviewService.ValidateAsync(1, story.Id);
        Assert.True(afterReview.CanApprove, string.Join("; ", afterReview.Issues));
        var approval = await reviewService.ApproveAsync(1, story.Id);
        Assert.True(approval.Success);
        Assert.Equal(StoryStatus.Approved, story.Status);
    }

    [Fact]
    public async Task Blocked_phase1_input_does_not_enter_phase2_or_create_downstream_artifacts()
    {
        var store = CreateEligibleStore(blockedTerm: "bạo lực");
        var service = new AIStoryInputService(store, new RuleBasedInputGuardrail(), CreateTokenQuotaService(store));

        var result = await service.SubmitAsync(1, ValidInput(topic: "Một câu chuyện bạo lực"));

        Assert.Equal("input_blocked", result.InputStatus);
        Assert.Equal(StoryStatus.Draft, Assert.Single(store.Items<Story>()).Status);
        Assert.Empty(store.Items<StoryGenerationJob>());
        Assert.Empty(store.Items<StoryVersion>());
        Assert.Empty(store.Items<StoryVocabulary>());
        Assert.Empty(store.Items<QuizItem>());
        Assert.Empty(store.Items<DiscussionQuestion>());
    }

    [Fact]
    public async Task Repeated_phase1_submission_is_idempotent_and_creates_one_outline_handoff()
    {
        var store = CreateEligibleStore();
        var service = new AIStoryInputService(store, new RuleBasedInputGuardrail(), CreateTokenQuotaService(store));
        var input = ValidInput();

        var first = await service.SubmitAsync(1, input);
        var second = await service.SubmitAsync(1, input);

        Assert.Equal(first.RequestId, second.RequestId);
        Assert.Single(store.Items<Story>());
        Assert.Single(store.Items<StoryGenerationRequest>());
        Assert.Single(store.Items<StoryGenerationJob>(), job => job.Operation == GenerationJobOperation.GenerateOutline);
    }

    [Fact]
    public async Task Phase2_approval_without_permission_does_not_create_phase3_handoff()
    {
        var store = CreateEligibleStore(includeApprovePermission: false);
        var ai = new WorkflowAIClient();
        var (story, outlineService, outline) = await RunToOutlineReviewAsync(store, ai);

        await Assert.ThrowsAsync<ForbiddenException>(() => outlineService.ApproveAsync(
            1,
            story.Id,
            outline.VersionNo,
            new ApproveOutlineRequestDto { ApprovalKey = "approval-without-permission" }));

        Assert.DoesNotContain(store.Items<StoryGenerationJob>(), job => job.Operation == GenerationJobOperation.GenerateContent);
        Assert.Equal(StoryStatus.OutlineReview, story.Status);
    }

    [Fact]
    public async Task Approved_phase2_outline_cannot_be_edited_before_phase3()
    {
        var store = CreateEligibleStore();
        var ai = new WorkflowAIClient();
        var (story, outlineService, outline) = await RunToOutlineReviewAsync(store, ai);
        await outlineService.ApproveAsync(
            1,
            story.Id,
            outline.VersionNo,
            new ApproveOutlineRequestDto { ApprovalKey = "locked-outline-approval" });

        await Assert.ThrowsAsync<ConflictException>(() => outlineService.EditAsync(
            1,
            story.Id,
            outline.VersionNo,
            new EditOutlineRequestDto
            {
                Title = "Changed",
                Opening = "Changed opening",
                Development = "Changed development",
                Ending = "Changed ending"
            }));
    }

    [Fact]
    public async Task Phase3_processes_artifacts_in_required_order_and_hands_off_to_phase4()
    {
        var (store, ai, story, stableVersion) = await RunToContentReviewAsync();

        Assert.Equal(StoryStatus.ContentReview, story.Status);
        Assert.True(stableVersion.IsCurrent);
        Assert.Equal(["outline", "content", "safety", "vocabulary", "quiz", "discussion"], ai.Calls);
        Assert.All(
            store.Items<StoryGenerationJob>().Where(job => job.Operation is
                GenerationJobOperation.GenerateContent or
                GenerationJobOperation.GenerateVocabulary or
                GenerationJobOperation.GenerateQuiz or
                GenerationJobOperation.GenerateDiscussion),
            job => Assert.Equal(GenerationJobStatus.Completed, job.Status));
        Assert.NotEmpty(store.Items<StoryVocabulary>());
        Assert.NotEmpty(store.Items<QuizItem>());
        Assert.NotEmpty(store.Items<DiscussionQuestion>());
    }

    [Fact]
    public async Task Phase3_safety_rejection_does_not_handoff_to_phase4()
    {
        var store = CreateEligibleStore();
        var ai = new WorkflowAIClient(blockContentSafety: true);
        var (story, outlineService, outline) = await RunToOutlineReviewAsync(store, ai);
        await outlineService.ApproveAsync(
            1,
            story.Id,
            outline.VersionNo,
            new ApproveOutlineRequestDto { ApprovalKey = "unsafe-content-approval" });
        var contentService = CreateContentService(store, ai);

        Assert.True(await contentService.ProcessNextAsync());

        var contentJob = store.Items<StoryGenerationJob>().Single(job => job.Operation == GenerationJobOperation.GenerateContent);
        Assert.Equal(GenerationJobStatus.Failed, contentJob.Status);
        Assert.Equal("CONTENT_SAFETY_BLOCKED", contentJob.ErrorCode);
        Assert.NotEqual(StoryStatus.ContentReview, story.Status);
        Assert.DoesNotContain(store.Items<StoryGenerationJob>(), job => job.Operation == GenerationJobOperation.GenerateVocabulary);
    }

    [Fact]
    public async Task Phase4_rejects_approval_when_raw_phase3_artifacts_are_incomplete()
    {
        var (store, ai, story, _) = await RunToContentReviewAsync();
        var reviewService = new StoryReviewService(store, ai, new InMemoryProposalCache());

        var validation = await reviewService.ValidateAsync(1, story.Id);

        Assert.False(validation.CanApprove);
        await Assert.ThrowsAsync<BadRequestException>(() => reviewService.ApproveAsync(1, story.Id));
        Assert.Equal(StoryStatus.ContentReview, story.Status);
    }

    [Fact]
    public async Task Phase4_archive_ends_workflow_without_approval()
    {
        var (store, ai, story, _) = await RunToContentReviewAsync();
        var reviewService = new StoryReviewService(store, ai, new InMemoryProposalCache());

        var result = await reviewService.ArchiveAsync(1, story.Id, new ArchiveRequestDto { Reason = "Parent chose not to use it" });

        Assert.True(result.Success);
        Assert.Equal("Archived", result.Status);
        Assert.Equal(StoryStatus.Archived, story.Status);
    }

    [Fact]
    public async Task Phase4_ai_partial_edit_proposal_applies_to_phase3_story()
    {
        var (store, ai, story, stableVersion) = await RunToContentReviewAsync();
        var cache = new InMemoryProposalCache();
        var reviewService = new StoryReviewService(store, ai, cache);
        var selectionStart = stableVersion.Content!.IndexOf("Lan", StringComparison.Ordinal);
        Assert.True(selectionStart >= 0);
        var proposal = await reviewService.CreateProposalAsync(1, story.Id, new PartialEditRequestDto
        {
            VersionId = stableVersion.Id,
            Selection = new TextSelectionDto { Start = selectionStart, EndExclusive = selectionStart + 3, Text = "Lan" },
            Instruction = "Mô tả nhân vật rõ hơn"
        });

        var proposalService = new ProposalService(cache, store);
        await proposalService.ApplyProposalAsync(1, story.Id, proposal.ProposalId);

        Assert.Contains("Lan tốt bụng", stableVersion.Content);
        Assert.Equal(VersionEditType.AiRefined, stableVersion.EditType);
        Assert.Equal(1, stableVersion.EditorUserId);
    }

    private static async Task<(Story Story, OutlineService Service, StoryVersion Outline)> RunToOutlineReviewAsync(
        WorkflowUnitOfWork store,
        WorkflowAIClient ai)
    {
        var inputService = new AIStoryInputService(store, new RuleBasedInputGuardrail(), CreateTokenQuotaService(store));
        await inputService.SubmitAsync(1, ValidInput());
        var story = store.Items<Story>().Single();
        var outlineService = new OutlineService(
            store,
            ai,
            new RuleBasedOutlineReviewGuardrail(),
            new OutlineFailureFinalizer(store));
        Assert.True(await outlineService.ProcessNextAsync());
        return (story, outlineService, store.Items<StoryVersion>().Single());
    }

    private static async Task<(WorkflowUnitOfWork Store, WorkflowAIClient AI, Story Story, StoryVersion StableVersion)> RunToContentReviewAsync()
    {
        var store = CreateEligibleStore();
        var ai = new WorkflowAIClient();
        var (story, outlineService, outline) = await RunToOutlineReviewAsync(store, ai);
        await outlineService.ApproveAsync(
            1,
            story.Id,
            outline.VersionNo,
            new ApproveOutlineRequestDto { ApprovalKey = "phase3-handoff-approval" });
        var contentService = CreateContentService(store, ai);
        Assert.True(await contentService.ProcessNextAsync());
        Assert.True(await contentService.ProcessNextAsync());
        Assert.True(await contentService.ProcessNextAsync());
        Assert.True(await contentService.ProcessNextAsync());
        return (store, ai, story, store.Items<StoryVersion>().Single(version => version.IsCurrent && version.Content is not null));
    }

    private static ContentGenerationService CreateContentService(WorkflowUnitOfWork store, WorkflowAIClient ai) =>
        new(
            store,
            ai,
            new PassingContentQualityEvaluator(),
            new ContentFailureFinalizer(store),
            new RecordingArtifactHandoff(store),
            new ContentGenerationOptions());

    private static SubmitAIStoryInputRequestDto ValidInput(string topic = "Tình bạn") => new()
    {
        ChildProfileId = 1,
        IdempotencyKey = "phase1-to-4-operation-0001",
        Topic = topic,
        Lesson = "Biết chia sẻ với bạn bè",
        VocabularyLevel = "level_2",
        CharacterMode = "ai_suggested",
        SettingMode = "ai_suggested",
        TargetLength = 500
    };

    private static ITokenQuotaService CreateTokenQuotaService(WorkflowUnitOfWork store) =>
        new TokenQuotaService(store, new NoopAuditLogWriter());

    private sealed class NoopAuditLogWriter : IAuditLogWriter
    {
        public Task LogAsync(
            int? actorUserId, string action, string entityType, int entityId,
            object? beforeState, object? afterState, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private static WorkflowUnitOfWork CreateEligibleStore(bool includeApprovePermission = true, string? blockedTerm = null)
    {
        var store = new WorkflowUnitOfWork();
        store.Seed(new UserAccount { Id = 1, Role = UserRole.Parent, Status = AccountStatus.LoggedIn });
        store.Seed(new ChildProfile
        {
            Id = 1,
            OwnerUserId = 1,
            Nickname = "Mây",
            AgeBand = AgeBand.Age_6_8,
            Language = "vi",
            Status = ChildProfileStatus.Active,
            Scope = ProfileScope.Personal
        });
        store.Seed(new LearningProfile { Id = 1, ChildProfileId = 1, ReadingLevel = 2 });
        store.Seed(new SafetyPolicy
        {
            Id = 1,
            ChildProfileId = 1,
            MaxStoryLength = 700,
            RequiredApprovalMode = ApprovalMode.AlwaysManual,
            ConsentRecorded = true,
            ConsentRecordedAt = DateTime.UtcNow,
            ConsentPolicyVersion = 1
        });
        store.Seed(new SupervisionRelationship
        {
            Id = 1,
            ChildProfileId = 1,
            SupervisorUserId = 1,
            SupervisorRole = SupervisorRole.Owner
        });
        store.Seed(new SupervisionPermission
        {
            Id = 1,
            SupervisionRelationshipId = 1,
            Permission = Permission.GenerateStory
        });
        if (includeApprovePermission)
        {
            store.Seed(new SupervisionPermission
            {
                Id = 2,
                SupervisionRelationshipId = 1,
                Permission = Permission.ApproveStory
            });
        }

        if (blockedTerm is not null)
        {
            var category = new ContentCategory { Id = 1, Code = "violence", DisplayName = blockedTerm, IsActive = true };
            store.Seed(category);
            store.Seed(new SafetyPolicyCategory
            {
                Id = 1,
                SafetyPolicyId = 1,
                ContentCategoryId = category.Id,
                ContentCategory = category,
                Rule = PolicyRule.Blocked
            });
        }

        return store;
    }

    private sealed class WorkflowAIClient(bool blockContentSafety = false) : IAIStoryGenerationClient
    {
        public List<string> Calls { get; } = [];

        public Task<GenerateOutlineResponse> GenerateOutlineAsync(GenerateOutlineRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add("outline");
            return Task.FromResult(new GenerateOutlineResponse
            {
                RequestId = request.RequestId,
                GenerationId = "outline-generation-1",
                Title = "Câu chuyện tình bạn",
                Outline = new StoryOutlineDto("Lan gặp Minh", "Hai bạn giúp nhau", "Hai bạn biết chia sẻ"),
                Metadata = new GenerationMetadataDto { ModelProvider = "test", Model = "workflow" }
            });
        }

        public Task<GenerateStoryContentResponse> GenerateStoryContentAsync(GenerateStoryContentRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add("content");
            return Task.FromResult(new GenerateStoryContentResponse
            {
                RequestId = request.RequestId,
                Story = new StoryContentDto
                {
                    Title = "Câu chuyện tình bạn",
                    Lesson = "Biết chia sẻ",
                    StorySections = [new StorySectionDto(1, "Câu chuyện", "Lan và Minh cùng chia sẻ sách trong rừng với bạn bè.")]
                }
            });
        }

        public Task<EvaluateContentSafetyResponse> EvaluateContentSafetyAsync(EvaluateContentSafetyRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add("safety");
            return Task.FromResult(new EvaluateContentSafetyResponse
            {
                RequestId = request.RequestId,
                IsAllowed = !blockContentSafety,
                CanRefine = false,
                ReasonCode = blockContentSafety ? "CONTENT_SAFETY_BLOCKED" : "CONTENT_SAFETY_ALLOWED"
            });
        }

        public Task<GenerateVocabularyResponse> GenerateVocabularyAsync(GenerateVocabularyRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add("vocabulary");
            return Task.FromResult(new GenerateVocabularyResponse
            {
                RequestId = request.RequestId,
                Items = [new GeneratedVocabularyItemDto("chia sẻ", "Cùng dùng với người khác")]
            });
        }

        public Task<GenerateQuizResponse> GenerateQuizAsync(GenerateQuizRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add("quiz");
            return Task.FromResult(new GenerateQuizResponse
            {
                RequestId = request.RequestId,
                Items =
                [
                    new StoryPlatform.Contracts.AI.Models.QuizItemDto
                    {
                        Type = "multiple_choice", Question = "Ai chia sẻ sách?", Options = ["Lan", "Nam"], CorrectOptionIndex = 0, CorrectAnswer = "Lan"
                    },
                    new StoryPlatform.Contracts.AI.Models.QuizItemDto
                    {
                        Type = "true_false", Question = "Hai bạn cùng đọc sách?", CorrectAnswer = "true", CorrectOptionIndex = -1
                    },
                    new StoryPlatform.Contracts.AI.Models.QuizItemDto
                    {
                        Type = "short_answer", Question = "Bài học là gì?", CorrectAnswer = "Biết chia sẻ", CorrectOptionIndex = -1
                    }
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

        public Task<RefineStoryContentResponse> RefineStoryContentAsync(RefineStoryContentRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add("refine");
            return Task.FromResult(new RefineStoryContentResponse
            {
                RequestId = request.RequestId,
                Story = request.Story with
                {
                    StorySections = [new StorySectionDto(1, string.Empty, "Lan tốt bụng")]
                }
            });
        }

        public Task<GenerateStoryResponse> GenerateStoryAsync(GenerateStoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RefineStoryResponse> RefineStoryAsync(RefineStoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EvaluateStoryResponse> EvaluateStoryAsync(EvaluateStoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class PassingContentQualityEvaluator : IContentQualityEvaluator
    {
        public ContentQualityResult Evaluate(
            StoryContentDto story,
            StoryOutlineDto approvedOutline,
            AcceptedAIStoryInputSnapshot input,
            AIStoryInputContextSnapshot context)
        {
            var pass = new ContentQualityGate(true, false, null, []);
            return new ContentQualityResult(true, pass, pass, pass, pass, pass);
        }
    }

    private sealed class OutlineFailureFinalizer(WorkflowUnitOfWork store) : IOutlineJobFailureFinalizer
    {
        public Task MarkFailedAsync(int jobId, string expectedConcurrencyToken, string errorCode, CancellationToken cancellationToken = default)
        {
            var job = store.Items<StoryGenerationJob>().Single(item => item.Id == jobId);
            if (job.ConcurrencyToken == expectedConcurrencyToken)
            {
                job.Status = GenerationJobStatus.Failed;
                job.Stage = JobStage.OutlineFailed;
                job.ErrorCode = errorCode;
            }
            return Task.CompletedTask;
        }
    }

    private sealed class ContentFailureFinalizer(WorkflowUnitOfWork store) : IContentGenerationJobFailureFinalizer
    {
        public Task MarkFailedAsync(int jobId, string expectedConcurrencyToken, string errorCode, CancellationToken cancellationToken = default)
        {
            var job = store.Items<StoryGenerationJob>().Single(item => item.Id == jobId);
            if (job.ConcurrencyToken == expectedConcurrencyToken)
            {
                job.Status = GenerationJobStatus.Failed;
                job.ErrorCode = errorCode;
            }
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingArtifactHandoff(WorkflowUnitOfWork store) : IStableVersionArtifactHandoffService
    {
        public List<(int StoryId, int VersionId, int? GenerationRequestId)> Calls { get; } = [];

        public async Task<int> QueueArtifactsAsync(
            int storyId, int storyVersionId, int requestedByUserId,
            int? generationRequestId, CancellationToken cancellationToken = default)
        {
            Calls.Add((storyId, storyVersionId, generationRequestId));
            var existing = store.Items<StoryGenerationJob>().FirstOrDefault(job =>
                job.StoryId == storyId
                && job.Operation == GenerationJobOperation.GenerateVocabulary
                && job.StoryVersionId == storyVersionId);
            if (existing is not null) return existing.Id;

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
            return jobEntity.Id;
        }
    }

    private sealed class WorkflowUnitOfWork : IUnitOfWork
    {
        private readonly Dictionary<Type, object> _repositories = [];

        public IGenericRepository<T> Repository<T>() where T : class =>
            (IGenericRepository<T>)(_repositories.TryGetValue(typeof(T), out var repository)
                ? repository
                : _repositories[typeof(T)] = new WorkflowRepository<T>());

        public void Seed<T>(T item) where T : class => ((WorkflowRepository<T>)Repository<T>()).Items.Add(item);
        public IReadOnlyList<T> Items<T>() where T : class => ((WorkflowRepository<T>)Repository<T>()).Items;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AcquireTransactionLockAsync(int resourceId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task CommitTransactionAsync(CancellationToken cancellationToken = default)
        {
            foreach (var request in Items<StoryGenerationRequest>())
            {
                if (request.Story is not null) request.StoryId = request.Story.Id;
                if (request.HandoffJob is not null) request.HandoffJobId = request.HandoffJob.Id;
            }
            foreach (var job in Items<StoryGenerationJob>().Where(item => item.StoryVersion is not null))
            {
                job.StoryVersionId = job.StoryVersion!.Id;
            }
            return Task.CompletedTask;
        }

        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class WorkflowRepository<T> : IGenericRepository<T> where T : class
    {
        public List<T> Items { get; } = [];
        public Task<T?> GetByIdAsync(object id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(item => item is BaseEntity entity && entity.Id == Convert.ToInt32(id)));
        public Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<T>>(Items);
        public Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate, string? includeProperties = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<T>>(Items.Where(predicate.Compile()).ToArray());
        public Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, string? includeProperties = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(predicate.Compile()));
        public Task<(IReadOnlyList<T> Items, int TotalCount)> GetPagedAsync(int pageIndex, int pageSize, Expression<Func<T, bool>>? filter = null, Func<IQueryable<T>, IOrderedQueryable<T>>? orderBy = null, string? includeProperties = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<T> AddAsync(T entity, CancellationToken cancellationToken = default)
        {
            if (entity is BaseEntity baseEntity && baseEntity.Id == 0)
            {
                baseEntity.Id = Items.OfType<BaseEntity>().Select(item => item.Id).DefaultIfEmpty().Max() + 1;
            }
            Items.Add(entity);
            return Task.FromResult(entity);
        }
        public async Task AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
        {
            foreach (var entity in entities) await AddAsync(entity, cancellationToken);
        }
        public void Update(T entity) { }
        public void Delete(T entity) => Items.Remove(entity);
        public void DeleteRange(IEnumerable<T> entities)
        {
            foreach (var entity in entities.ToArray()) Items.Remove(entity);
        }
        public Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(predicate is null ? Items.Count : Items.Count(predicate.Compile()));
        public Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.Any(predicate.Compile()));
        public IQueryable<T> Query() => Items.AsQueryable();
    }
}
