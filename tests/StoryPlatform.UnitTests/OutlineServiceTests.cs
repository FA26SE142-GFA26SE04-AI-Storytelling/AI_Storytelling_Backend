using System.Linq.Expressions;
using System.Text.Json;
using StoryPlatform.Application.Abstractions.AI;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AIStoryInput.Models;
using StoryPlatform.Application.Features.Outline.DTOs;
using StoryPlatform.Application.Features.Outline.Guardrails;
using StoryPlatform.Application.Features.Outline.Interfaces;
using StoryPlatform.Application.Features.Outline.Services;
using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Contracts.AI.Responses;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class OutlineServiceTests
{
    [Fact]
    public async Task Approved_outline_cannot_be_edited_or_regenerated_after_content_handoff()
    {
        var db = Seed();
        var service = Service(db);
        await service.ProcessNextAsync();
        await service.ApproveAsync(1, 1, 1, new ApproveOutlineRequestDto { ApprovalKey = "approval-pinned-1" });
        await Assert.ThrowsAsync<ConflictException>(() => service.EditAsync(1, 1, 1,
            new EditOutlineRequestDto { Title = "Changed", Opening = "Opening", Development = "Development", Ending = "Ending" }));
        await Assert.ThrowsAsync<ConflictException>(() => service.RegenerateAsync(1, 1, 1,
            new RegenerateOutlineRequestDto { OperationKey = "regenerate-pinned-1" }));
        Assert.Single(db.Items<StoryVersion>());
    }

    [Fact]
    public async Task Worker_creates_only_one_current_outline_version()
    {
        var unitOfWork = Seed();
        var service = Service(unitOfWork);

        Assert.True(await service.ProcessNextAsync());

        var version = Assert.Single(unitOfWork.Items<StoryVersion>());
        Assert.Equal(VersionEditType.Initial, version.EditType);
        Assert.True(version.IsCurrent);
        Assert.Null(version.Content);
        Assert.Equal(StoryStatus.OutlineReview, unitOfWork.Items<Story>().Single().Status);
        var job = unitOfWork.Items<StoryGenerationJob>().Single();
        Assert.Equal(GenerationJobStatus.Completed, job.Status);
        Assert.Equal(JobStage.OutlineGenerated, job.Stage);
        Assert.Equal(version.Id, job.StoryVersionId);
    }

    [Fact]
    public async Task Human_edit_preserves_v1_and_creates_current_v2()
    {
        var unitOfWork = Seed();
        var service = Service(unitOfWork);
        await service.ProcessNextAsync();

        var edited = await service.EditAsync(1, 1, 1, new EditOutlineRequestDto
        {
            Title = "Tiêu đề mới",
            Opening = "Mở đầu mới",
            Development = "Phát triển mới",
            Ending = "Kết thúc mới"
        });

        Assert.Equal(2, edited.VersionNo);
        Assert.Equal(VersionEditType.HumanEdited.ToString(), edited.EditType);
        var versions = unitOfWork.Items<StoryVersion>().OrderBy(item => item.VersionNo).ToArray();
        Assert.False(versions[0].IsCurrent);
        Assert.True(versions[1].IsCurrent);
        Assert.Null(versions[1].Content);
    }

    [Fact]
    public async Task Approval_requires_separate_approve_permission()
    {
        var unitOfWork = Seed(includeApprovePermission: false);
        var service = Service(unitOfWork);
        await service.ProcessNextAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() => service.ApproveAsync(
            1, 1, 1, new ApproveOutlineRequestDto { ApprovalKey = "approve-0001" }));

        Assert.DoesNotContain(unitOfWork.Items<StoryGenerationJob>(),
            item => item.Operation == GenerationJobOperation.GenerateContent);
    }

    [Fact]
    public async Task Approval_marks_exact_version_and_creates_content_handoff()
    {
        var unitOfWork = Seed();
        var service = Service(unitOfWork);
        await service.ProcessNextAsync();

        await service.ApproveAsync(1, 1, 1, new ApproveOutlineRequestDto { ApprovalKey = "approve-0001" });

        var version = unitOfWork.Items<StoryVersion>().Single();
        Assert.Equal(1, version.OutlineApprovedByUserId);
        Assert.NotNull(version.OutlineApprovedAt);
        var handoff = Assert.Single(unitOfWork.Items<StoryGenerationJob>(),
            item => item.Operation == GenerationJobOperation.GenerateContent);
        Assert.Equal(version.Id, handoff.BaseStoryVersionId);
        Assert.Null(handoff.StoryVersionId);
        Assert.Equal(JobStage.ContentPending, handoff.Stage);
        Assert.Equal(GenerationJobStatus.Pending, handoff.Status);
        Assert.True(unitOfWork.LockCount > 0);
    }

    [Fact]
    public async Task Worker_reclaims_processing_job_after_lease_expired()
    {
        var unitOfWork = Seed();
        var job = unitOfWork.Items<StoryGenerationJob>().Single();
        job.Status = GenerationJobStatus.Processing;
        job.Stage = JobStage.OutlineGenerating;
        job.LeaseExpiresAt = DateTime.UtcNow.AddMinutes(-1);

        Assert.True(await Service(unitOfWork).ProcessNextAsync());

        Assert.Equal(GenerationJobStatus.Completed, job.Status);
        Assert.Null(job.LeaseExpiresAt);
        Assert.Single(unitOfWork.Items<StoryVersion>());
    }

    [Fact]
    public async Task Failed_initial_outline_can_be_retried_idempotently()
    {
        var unitOfWork = Seed();
        var failed = unitOfWork.Items<StoryGenerationJob>().Single();
        failed.Status = GenerationJobStatus.Failed;
        failed.Stage = JobStage.OutlineFailed;

        var service = Service(unitOfWork);
        var request = new RetryOutlineRequestDto { OperationKey = "retry-initial-0001" };
        await service.RetryInitialAsync(1, 1, request);
        await service.RetryInitialAsync(1, 1, request);

        var retry = Assert.Single(unitOfWork.Items<StoryGenerationJob>(),
            item => item.OperationKey == request.OperationKey);
        Assert.Equal(GenerationJobStatus.Pending, retry.Status);
        Assert.Equal(JobStage.OutlinePending, retry.Stage);
    }

    [Fact]
    public async Task Vietnamese_policy_match_term_blocks_human_edit()
    {
        var unitOfWork = Seed();
        await Service(unitOfWork).ProcessNextAsync();
        var context = new AIStoryInputContextSnapshot(
            1, "6-8", 2, "level_2", "vi", 700, "always_manual", [], [], [], ["violence"],
            BlockedCategoryTerms: ["bạo lực"]);
        unitOfWork.Items<StoryGenerationRequest>().Single().ContextSnapshotJson =
            JsonSerializer.Serialize(context, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        await Assert.ThrowsAsync<BadRequestException>(() => Service(unitOfWork).EditAsync(1, 1, 1,
            new EditOutlineRequestDto
            {
                Title = "Một cuộc phiêu lưu bạo lực",
                Opening = "Mở đầu",
                Development = "Phát triển",
                Ending = "Kết thúc"
            }));
    }

    [Fact]
    public async Task Generation_failure_is_finalized_by_separate_finalizer()
    {
        var unitOfWork = Seed();

        Assert.True(await Service(unitOfWork, new ThrowingAIClient()).ProcessNextAsync());

        var job = unitOfWork.Items<StoryGenerationJob>().Single();
        Assert.Equal(GenerationJobStatus.Failed, job.Status);
        Assert.Equal("OUTLINE_GENERATION_FAILED", job.ErrorCode);
        Assert.Null(job.LeaseExpiresAt);
    }

    [Fact]
    public async Task Persistence_failure_finalizes_using_last_committed_claim_token()
    {
        var unitOfWork = Seed();
        unitOfWork.FailCommitNumber = 2;
        var finalizer = new RecordingFailureFinalizer();
        var service = Service(unitOfWork, failureFinalizer: finalizer);

        Assert.True(await service.ProcessNextAsync());

        Assert.Equal(unitOfWork.CommittedJobTokens.Single(), finalizer.ExpectedConcurrencyToken);
        Assert.NotEqual(unitOfWork.Items<StoryGenerationJob>().Single().ConcurrencyToken,
            finalizer.ExpectedConcurrencyToken);
    }

    [Fact]
    public async Task Unsafe_human_edit_does_not_create_version()
    {
        var unitOfWork = Seed();
        var service = Service(unitOfWork);
        await service.ProcessNextAsync();

        await Assert.ThrowsAsync<BadRequestException>(() => service.EditAsync(1, 1, 1, new EditOutlineRequestDto
        {
            Title = "Liên hệ child@example.com",
            Opening = "A",
            Development = "B",
            Ending = "C"
        }));

        Assert.Single(unitOfWork.Items<StoryVersion>());
    }

    private static OutlineService Service(
        FakeUnitOfWork unitOfWork,
        IAIStoryGenerationClient? aiClient = null,
        IOutlineJobFailureFinalizer? failureFinalizer = null) =>
        new(unitOfWork, aiClient ?? new FakeAIClient(), new RuleBasedOutlineReviewGuardrail(),
            failureFinalizer ?? new FakeFailureFinalizer(unitOfWork));

    internal static FakeUnitOfWork Seed(bool includeApprovePermission = true)
    {
        var unitOfWork = new FakeUnitOfWork();
        unitOfWork.Seed(new UserAccount { Id = 1, Role = UserRole.Parent, Status = AccountStatus.LoggedIn });
        unitOfWork.Seed(new Story
        {
            Id = 1, ChildProfileId = 1, AuthorUserId = 1, Source = StorySource.Ai, Status = StoryStatus.Draft
        });
        unitOfWork.Seed(new SupervisionRelationship
        {
            Id = 1, ChildProfileId = 1, SupervisorUserId = 1, SupervisorRole = SupervisorRole.Owner
        });
        unitOfWork.Seed(new SupervisionPermission
        {
            Id = 1, SupervisionRelationshipId = 1, Permission = Permission.GenerateStory
        });
        if (includeApprovePermission)
        {
            unitOfWork.Seed(new SupervisionPermission
            {
                Id = 2, SupervisionRelationshipId = 1, Permission = Permission.ApproveStory
            });
        }

        var input = new AcceptedAIStoryInputSnapshot(
            "Tình bạn", null, "ai_suggested", [], "ai_suggested", null,
            "Biết chia sẻ", "level_2", "vi", 500);
        var context = new AIStoryInputContextSnapshot(
            1, "6-8", 2, "level_2", "vi", 700, "always_manual", [], [], [], []);
        unitOfWork.Seed(new StoryGenerationRequest
        {
            Id = 1,
            StoryId = 1,
            SubmittedByUserId = 1,
            IdempotencyKey = "input-0001",
            InputFingerprint = new string('a', 64),
            ContextFingerprint = new string('b', 64),
            ContextSnapshotJson = JsonSerializer.Serialize(context, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            AcceptedInputJson = JsonSerializer.Serialize(input, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            Status = GenerationInputStatus.InputAccepted
        });
        unitOfWork.Seed(new StoryGenerationJob
        {
            Id = 1,
            StoryId = 1,
            GenerationRequestId = 1,
            RequestedByUserId = 1,
            Operation = GenerationJobOperation.GenerateOutline,
            Stage = JobStage.OutlinePending,
            Status = GenerationJobStatus.Pending,
            MaxAttempts = 3,
            StartedAt = DateTime.UtcNow
        });
        return unitOfWork;
    }

    private sealed class FakeAIClient : IAIStoryGenerationClient
    {
        public Task<GenerateOutlineResponse> GenerateOutlineAsync(GenerateOutlineRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GenerateOutlineResponse
            {
                RequestId = request.RequestId,
                GenerationId = "generation-1",
                Title = "Cáo nhỏ tốt bụng",
                Outline = new StoryOutlineDto("Mở đầu", "Phát triển", "Kết thúc"),
                Metadata = new GenerationMetadataDto { ModelProvider = "test", Model = "test" }
            });

        public Task<GenerateStoryResponse> GenerateStoryAsync(GenerateStoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RefineStoryResponse> RefineStoryAsync(RefineStoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EvaluateStoryResponse> EvaluateStoryAsync(EvaluateStoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ThrowingAIClient : IAIStoryGenerationClient
    {
        public Task<GenerateOutlineResponse> GenerateOutlineAsync(GenerateOutlineRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("provider failed");
        public Task<GenerateStoryResponse> GenerateStoryAsync(GenerateStoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RefineStoryResponse> RefineStoryAsync(RefineStoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EvaluateStoryResponse> EvaluateStoryAsync(EvaluateStoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    internal sealed class FakeUnitOfWork : IUnitOfWork
    {
        private readonly Dictionary<Type, object> _repositories = [];
        public int LockCount { get; private set; }
        public int? FailCommitNumber { get; set; }
        public int CommitCount { get; private set; }
        public List<string> CommittedJobTokens { get; } = [];
        public IGenericRepository<T> Repository<T>() where T : class =>
            (IGenericRepository<T>)(_repositories.TryGetValue(typeof(T), out var repository)
                ? repository
                : _repositories[typeof(T)] = new FakeRepository<T>());

        public void Seed<T>(T item) where T : class => ((FakeRepository<T>)Repository<T>()).Items.Add(item);
        public IReadOnlyList<T> Items<T>() where T : class => ((FakeRepository<T>)Repository<T>()).Items;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AcquireTransactionLockAsync(int resourceId, CancellationToken cancellationToken = default)
        {
            LockCount++;
            return Task.CompletedTask;
        }
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default)
        {
            CommitCount++;
            if (FailCommitNumber == CommitCount)
            {
                throw new InvalidOperationException("simulated commit failure");
            }
            foreach (var job in Items<StoryGenerationJob>().Where(item => item.StoryVersion is not null))
            {
                job.StoryVersionId = job.StoryVersion!.Id;
            }
            var currentJob = Items<StoryGenerationJob>().FirstOrDefault(item => item.Status == GenerationJobStatus.Processing);
            if (currentJob is not null)
            {
                CommittedJobTokens.Add(currentJob.ConcurrencyToken);
            }
            return Task.CompletedTask;
        }
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeFailureFinalizer(FakeUnitOfWork unitOfWork) : IOutlineJobFailureFinalizer
    {
        public Task MarkFailedAsync(
            int jobId,
            string expectedConcurrencyToken,
            string errorCode,
            CancellationToken cancellationToken = default)
        {
            var job = unitOfWork.Items<StoryGenerationJob>().Single(item => item.Id == jobId);
            if (job.Status == GenerationJobStatus.Processing && job.ConcurrencyToken == expectedConcurrencyToken)
            {
                job.Status = GenerationJobStatus.Failed;
                job.Stage = JobStage.OutlineFailed;
                job.ErrorCode = errorCode;
                job.LeaseExpiresAt = null;
            }
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingFailureFinalizer : IOutlineJobFailureFinalizer
    {
        public string? ExpectedConcurrencyToken { get; private set; }

        public Task MarkFailedAsync(
            int jobId,
            string expectedConcurrencyToken,
            string errorCode,
            CancellationToken cancellationToken = default)
        {
            ExpectedConcurrencyToken = expectedConcurrencyToken;
            return Task.CompletedTask;
        }
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
            if (entity is BaseEntity baseEntity && baseEntity.Id == 0)
            {
                baseEntity.Id = Items.OfType<BaseEntity>().Select(item => item.Id).DefaultIfEmpty().Max() + 1;
            }
            Items.Add(entity);
            return Task.FromResult(entity);
        }
        public async Task AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default) { foreach (var entity in entities) await AddAsync(entity, cancellationToken); }
        public void Update(T entity) { }
        public void Delete(T entity) => Items.Remove(entity);
        public void DeleteRange(IEnumerable<T> entities) { foreach (var entity in entities.ToArray()) Items.Remove(entity); }
        public Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default) => Task.FromResult(predicate is null ? Items.Count : Items.Count(predicate.Compile()));
        public Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) => Task.FromResult(Items.Any(predicate.Compile()));
        public IQueryable<T> Query() => Items.AsQueryable();
    }
}
