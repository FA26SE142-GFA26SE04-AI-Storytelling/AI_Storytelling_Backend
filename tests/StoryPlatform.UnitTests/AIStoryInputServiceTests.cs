using System.Linq.Expressions;
using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AIStoryInput.DTOs;
using StoryPlatform.Application.Features.AIStoryInput.Guardrails;
using StoryPlatform.Application.Features.AIStoryInput.Models;
using StoryPlatform.Application.Features.AIStoryInput.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class AIStoryInputServiceTests
{
    [Fact]
    public async Task Loading_context_does_not_create_business_records()
    {
        var unitOfWork = CreateEligibleUnitOfWork();
        var service = new AIStoryInputService(unitOfWork, new RuleBasedInputGuardrail());

        var context = await service.GetContextAsync(1, 1);

        Assert.Equal(700, context.MaximumLength);
        Assert.Empty(unitOfWork.Items<Story>());
        Assert.Empty(unitOfWork.Items<StoryGenerationRequest>());
        Assert.Empty(unitOfWork.Items<StoryGenerationJob>());
    }

    [Fact]
    public async Task Missing_generate_permission_creates_nothing()
    {
        var unitOfWork = CreateEligibleUnitOfWork(includePermission: false);
        var service = new AIStoryInputService(unitOfWork, new RuleBasedInputGuardrail());

        await Assert.ThrowsAsync<ForbiddenException>(() => service.SubmitAsync(1, ValidRequest()));

        Assert.Empty(unitOfWork.Items<Story>());
        Assert.Empty(unitOfWork.Items<StoryGenerationRequest>());
        Assert.Empty(unitOfWork.Items<StoryGenerationJob>());
    }

    [Fact]
    public async Task Target_above_policy_maximum_creates_nothing()
    {
        var unitOfWork = CreateEligibleUnitOfWork();
        var service = new AIStoryInputService(unitOfWork, new RuleBasedInputGuardrail());

        await Assert.ThrowsAsync<BadRequestException>(() => service.SubmitAsync(1, ValidRequest(targetLength: 701)));

        Assert.Empty(unitOfWork.Items<Story>());
        Assert.Empty(unitOfWork.Items<StoryGenerationRequest>());
    }

    [Fact]
    public async Task Organization_context_uses_the_more_restrictive_policy()
    {
        var unitOfWork = CreateEligibleUnitOfWork();
        var child = unitOfWork.Items<ChildProfile>().Single();
        child.Scope = ProfileScope.Organization;
        child.OrganizationId = 10;
        unitOfWork.Items<SafetyPolicy>().Single().RequiredApprovalMode = ApprovalMode.AutoPublishOnThreshold;
        var category = new ContentCategory { Id = 3, Code = "violence", DisplayName = "Bạo lực", IsActive = true };
        unitOfWork.Seed(category);
        unitOfWork.Seed(new SafetyPolicyCategory
        {
            Id = 3,
            SafetyPolicyId = 1,
            ContentCategoryId = 3,
            ContentCategory = category,
            Rule = PolicyRule.Allowed
        });
        unitOfWork.Seed(new OrgSafetyPolicyTemplate
        {
            Id = 1,
            OrganizationId = 10,
            MaxStoryLengthBaseline = 600,
            RequiredApprovalModeDefault = ApprovalMode.AlwaysManual
        });
        unitOfWork.Seed(new OrgSafetyPolicyCategory
        {
            Id = 1,
            OrgSafetyPolicyTemplateId = 1,
            ContentCategoryId = 3,
            ContentCategory = category,
            Rule = PolicyRule.Blocked
        });
        var service = new AIStoryInputService(unitOfWork, new RuleBasedInputGuardrail());

        var context = await service.GetContextAsync(1, 1);

        Assert.Equal(600, context.MaximumLength);
        Assert.Equal("always_manual", context.RequiredApprovalMode);
        Assert.Contains("violence", context.BlockedCategoryCodes);
        Assert.DoesNotContain("violence", context.AllowedCategoryCodes);
    }

    [Fact]
    public async Task Allow_creates_one_draft_request_snapshot_and_handoff()
    {
        var unitOfWork = CreateEligibleUnitOfWork();
        var service = new AIStoryInputService(unitOfWork, new RuleBasedInputGuardrail());

        var result = await service.SubmitAsync(1, ValidRequest());

        var story = Assert.Single(unitOfWork.Items<Story>());
        Assert.Null(story.Title);
        Assert.Equal(StoryStatus.Draft, story.Status);
        Assert.Equal(StorySource.Ai, story.Source);
        var request = Assert.Single(unitOfWork.Items<StoryGenerationRequest>());
        Assert.Equal(GenerationInputStatus.InputAccepted, request.Status);
        Assert.NotNull(request.AcceptedInputJson);
        var snapshot = JsonSerializer.Deserialize<AcceptedAIStoryInputSnapshot>(request.AcceptedInputJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("Tình bạn", snapshot!.Topic);
        Assert.Single(unitOfWork.Items<StoryGenerationJob>());
        Assert.Empty(unitOfWork.Items<StoryVersion>());
        Assert.Equal("input_accepted", result.InputStatus);
        Assert.Equal("pending_dispatch", result.HandoffStatus);
    }

    [Fact]
    public async Task Block_keeps_draft_without_snapshot_or_handoff()
    {
        var unitOfWork = CreateEligibleUnitOfWork(blockedTerm: "bạo lực");
        var service = new AIStoryInputService(unitOfWork, new RuleBasedInputGuardrail());

        var result = await service.SubmitAsync(1, ValidRequest(topic: "Một câu chuyện bạo lực"));

        Assert.Single(unitOfWork.Items<Story>());
        var request = Assert.Single(unitOfWork.Items<StoryGenerationRequest>());
        Assert.Equal(GenerationInputStatus.InputBlocked, request.Status);
        Assert.Null(request.AcceptedInputJson);
        Assert.Empty(unitOfWork.Items<StoryGenerationJob>());
        Assert.Equal("input_blocked", result.InputStatus);
    }

    [Fact]
    public async Task Same_idempotency_key_and_payload_returns_existing_request()
    {
        var unitOfWork = CreateEligibleUnitOfWork();
        var service = new AIStoryInputService(unitOfWork, new RuleBasedInputGuardrail());
        var input = ValidRequest();

        var first = await service.SubmitAsync(1, input);
        var second = await service.SubmitAsync(1, input);

        Assert.Equal(first.RequestId, second.RequestId);
        Assert.Single(unitOfWork.Items<Story>());
        Assert.Single(unitOfWork.Items<StoryGenerationRequest>());
        Assert.Single(unitOfWork.Items<StoryGenerationJob>());
    }

    [Fact]
    public async Task Reusing_idempotency_key_for_changed_payload_is_conflict()
    {
        var unitOfWork = CreateEligibleUnitOfWork();
        var service = new AIStoryInputService(unitOfWork, new RuleBasedInputGuardrail());
        await service.SubmitAsync(1, ValidRequest());

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.SubmitAsync(1, ValidRequest(topic: "Một ý tưởng khác")));

        Assert.Single(unitOfWork.Items<StoryGenerationRequest>());
    }

    [Fact]
    public async Task Technical_failure_can_retry_same_request_without_new_story()
    {
        var unitOfWork = CreateEligibleUnitOfWork();
        var failingService = new AIStoryInputService(unitOfWork, new ThrowingGuardrail());

        var failed = await failingService.SubmitAsync(1, ValidRequest());
        Assert.Equal("input_check_failed", failed.InputStatus);
        Assert.True(failed.CanRetry);

        var retryService = new AIStoryInputService(unitOfWork, new RuleBasedInputGuardrail());
        var accepted = await retryService.RetryAsync(1, failed.StoryId, failed.RequestId, ValidRetry());

        Assert.Equal("input_accepted", accepted.InputStatus);
        Assert.Equal(2, accepted.AttemptCount);
        Assert.Single(unitOfWork.Items<Story>());
        Assert.Single(unitOfWork.Items<StoryGenerationRequest>());
        Assert.Single(unitOfWork.Items<StoryGenerationJob>());
    }

    private static SubmitAIStoryInputRequestDto ValidRequest(string topic = "Tình bạn", int targetLength = 500) => new()
    {
        ChildProfileId = 1,
        IdempotencyKey = "operation-0001",
        Topic = topic,
        Lesson = "Biết giúp đỡ bạn bè",
        VocabularyLevel = "level_2",
        CharacterMode = "ai_suggested",
        SettingMode = "ai_suggested",
        TargetLength = targetLength
    };

    private static RetryAIStoryInputRequestDto ValidRetry() => new()
    {
        RetryKey = "retry-0001",
        Topic = "Tình bạn",
        Lesson = "Biết giúp đỡ bạn bè",
        VocabularyLevel = "level_2",
        CharacterMode = "ai_suggested",
        SettingMode = "ai_suggested",
        TargetLength = 500
    };

    private static FakeUnitOfWork CreateEligibleUnitOfWork(bool includePermission = true, string? blockedTerm = null)
    {
        var unitOfWork = new FakeUnitOfWork();
        unitOfWork.Seed(new UserAccount { Id = 1, Role = UserRole.Parent, Status = AccountStatus.LoggedIn });
        unitOfWork.Seed(new ChildProfile
        {
            Id = 1,
            OwnerUserId = 1,
            Nickname = "Mây",
            AgeBand = AgeBand.Age_6_8,
            Language = "vi",
            Status = ChildProfileStatus.Active,
            Scope = ProfileScope.Personal
        });
        unitOfWork.Seed(new LearningProfile { Id = 1, ChildProfileId = 1, ReadingLevel = 2 });
        unitOfWork.Seed(new SafetyPolicy
        {
            Id = 1,
            ChildProfileId = 1,
            MaxStoryLength = 700,
            RequiredApprovalMode = ApprovalMode.AlwaysManual
        });
        unitOfWork.Seed(new SupervisionRelationship
        {
            Id = 1,
            ChildProfileId = 1,
            SupervisorUserId = 1,
            SupervisorRole = SupervisorRole.Owner
        });
        if (includePermission)
        {
            unitOfWork.Seed(new SupervisionPermission
            {
                Id = 1,
                SupervisionRelationshipId = 1,
                Permission = Permission.GenerateStory
            });
        }

        if (blockedTerm is not null)
        {
            var category = new ContentCategory { Id = 1, Code = "violence", DisplayName = blockedTerm, IsActive = true };
            unitOfWork.Seed(category);
            unitOfWork.Seed(new SafetyPolicyCategory
            {
                Id = 1,
                SafetyPolicyId = 1,
                ContentCategoryId = 1,
                ContentCategory = category,
                Rule = PolicyRule.Blocked
            });
        }

        return unitOfWork;
    }

    private sealed class ThrowingGuardrail : IInputGuardrail
    {
        public Task<InputGuardrailResult> CheckAsync(InputGuardrailRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Provider details must not escape.");
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        private readonly Dictionary<Type, object> _repositories = [];

        public IGenericRepository<T> Repository<T>() where T : class
        {
            if (!_repositories.TryGetValue(typeof(T), out var repository))
            {
                repository = new FakeRepository<T>();
                _repositories[typeof(T)] = repository;
            }

            return (IGenericRepository<T>)repository;
        }

        public void Seed<T>(T item) where T : class => ((FakeRepository<T>)Repository<T>()).Items.Add(item);
        public IReadOnlyList<T> Items<T>() where T : class => ((FakeRepository<T>)Repository<T>()).Items;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task CommitTransactionAsync(CancellationToken cancellationToken = default)
        {
            foreach (var request in Items<StoryGenerationRequest>())
            {
                if (request.Story is not null)
                {
                    request.StoryId = request.Story.Id;
                }

                if (request.HandoffJob is not null)
                {
                    request.HandoffJobId = request.HandoffJob.Id;
                }
            }

            return Task.CompletedTask;
        }

        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeRepository<T> : IGenericRepository<T> where T : class
    {
        public List<T> Items { get; } = [];

        public Task<T?> GetByIdAsync(object id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(item => GetId(item) == Convert.ToInt32(id)));

        public Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<T>>(Items);

        public Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate, string? includeProperties = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<T>>(Items.Where(predicate.Compile()).ToArray());

        public Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, string? includeProperties = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(predicate.Compile()));

        public Task<(IReadOnlyList<T> Items, int TotalCount)> GetPagedAsync(int pageIndex, int pageSize, Expression<Func<T, bool>>? filter = null, Func<IQueryable<T>, IOrderedQueryable<T>>? orderBy = null, string? includeProperties = null, CancellationToken cancellationToken = default)
        {
            var query = Items.AsQueryable();
            if (filter is not null) query = query.Where(filter);
            if (orderBy is not null) query = orderBy(query);
            var all = query.ToArray();
            return Task.FromResult(((IReadOnlyList<T>)all.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToArray(), all.Length));
        }

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
        public void DeleteRange(IEnumerable<T> entities) { foreach (var entity in entities.ToArray()) Items.Remove(entity); }
        public Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(predicate is null ? Items.Count : Items.Count(predicate.Compile()));
        public Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.Any(predicate.Compile()));
        public IQueryable<T> Query() => Items.AsQueryable();

        private static int GetId(T item) => item is BaseEntity entity ? entity.Id : 0;
    }
}
