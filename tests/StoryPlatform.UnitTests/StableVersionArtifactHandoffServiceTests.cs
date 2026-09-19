using System.Linq.Expressions;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Learning.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.Learning.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Safety.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.Safety.Interfaces;
using StoryPlatform.Application.Features.ExistingStories.Interfaces;
using StoryPlatform.Application.Features.ExistingStories.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class StableVersionArtifactHandoffServiceTests
{
    [Fact]
    public async Task QueueArtifacts_ForExistingStory_CreatesVocabularyJobOnly()
    {
        var store = Seed();
        var service = BuildService(store);

        var jobId = await service.QueueArtifactsAsync(
            storyId: 1, storyVersionId: 1, requestedByUserId: 1, generationRequestId: null);

        Assert.Single(store.Items<StoryGenerationJob>(), job => job.Operation == GenerationJobOperation.GenerateVocabulary);
        Assert.DoesNotContain(store.Items<StoryGenerationJob>(),
            job => job.Operation == GenerationJobOperation.GenerateContent);
        Assert.DoesNotContain(store.Items<StoryGenerationJob>(),
            job => job.Operation == GenerationJobOperation.GenerateOutline);
        Assert.Single(store.Items<StoryGenerationJob>(),
            job => job.StoryVersionId == 1 && job.Operation == GenerationJobOperation.GenerateVocabulary && job.Id == jobId);
    }

    [Fact]
    public async Task QueueArtifacts_CreatesVirtualGenerationRequest_WhenNoneExists()
    {
        var store = Seed();
        var service = BuildService(store);

        await service.QueueArtifactsAsync(1, 1, 1, null);

        Assert.Single(store.Items<StoryGenerationRequest>());
        var request = store.Items<StoryGenerationRequest>().Single();
        Assert.Equal(GenerationInputStatus.InputAccepted, request.Status);
        Assert.False(string.IsNullOrWhiteSpace(request.ContextSnapshotJson));
        Assert.False(string.IsNullOrWhiteSpace(request.AcceptedInputJson));
    }

    [Fact]
    public async Task QueueArtifacts_ReusesExistingGenerationRequest()
    {
        var store = Seed();
        store.Seed(new StoryGenerationRequest
        {
            Id = 1, StoryId = 1, SubmittedByUserId = 1, IdempotencyKey = "existing-key",
            InputFingerprint = "abc", ContextFingerprint = "def",
            ContextSnapshotJson = "{}", AcceptedInputJson = "{\"Topic\":\"X\"}",
            Status = GenerationInputStatus.InputAccepted, AttemptCount = 0, MaxAttempts = 1
        });
        var service = BuildService(store);

        await service.QueueArtifactsAsync(1, 1, 1, null);

        Assert.Single(store.Items<StoryGenerationRequest>());
        Assert.Equal(1, store.Items<StoryGenerationJob>().Single().GenerationRequestId);
    }

    [Theory]
    [InlineData(GenerationInputStatus.InputBlocked)]
    [InlineData(GenerationInputStatus.InputCheckFailed)]
    [InlineData(GenerationInputStatus.PendingInput)]
    [InlineData(GenerationInputStatus.CheckingInput)]
    public async Task QueueArtifacts_RejectsProvidedRequestThatWasNotAccepted(GenerationInputStatus status)
    {
        var store = Seed();
        store.Seed(new StoryGenerationRequest
        {
            Id = 7, StoryId = 1, SubmittedByUserId = 1, IdempotencyKey = $"request-{status}",
            InputFingerprint = "abc", ContextFingerprint = "def",
            ContextSnapshotJson = "{}", AcceptedInputJson = "{}",
            Status = status, AttemptCount = 1, MaxAttempts = 1
        });
        var service = BuildService(store);

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.QueueArtifactsAsync(1, 1, 1, generationRequestId: 7));

        Assert.Empty(store.Items<StoryGenerationJob>());
    }

    [Fact]
    public async Task QueueArtifacts_IsIdempotent_OnSecondCall()
    {
        var store = Seed();
        var service = BuildService(store);

        var firstJobId = await service.QueueArtifactsAsync(1, 1, 1, null);
        var secondJobId = await service.QueueArtifactsAsync(1, 1, 1, null);

        Assert.Equal(firstJobId, secondJobId);
        Assert.Single(store.Items<StoryGenerationJob>(),
            job => job.Operation == GenerationJobOperation.GenerateVocabulary);
    }

    [Fact]
    public async Task QueueArtifacts_RejectsNotCurrentVersion()
    {
        var store = Seed();
        var v1 = store.Items<StoryVersion>().Single();
        v1.IsCurrent = false;
        var service = BuildService(store);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.QueueArtifactsAsync(1, 1, 1, null));
    }

    [Fact]
    public async Task QueueArtifacts_RejectsBlankContentVersion()
    {
        var store = Seed();
        var v1 = store.Items<StoryVersion>().Single();
        v1.Content = null;
        var service = BuildService(store);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.QueueArtifactsAsync(1, 1, 1, null));
    }

    [Fact]
    public async Task QueueArtifacts_RejectsNonExistentStory()
    {
        var store = Seed();
        var service = BuildService(store);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.QueueArtifactsAsync(99, 1, 1, null));
    }

    [Fact]
    public async Task QueueArtifacts_RejectsRejectsNonExistingStoryStatus()
    {
        var store = Seed();
        var story = store.Items<Story>().Single();
        story.Status = StoryStatus.Approved;
        var service = BuildService(store);

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.QueueArtifactsAsync(1, 1, 1, null));
    }

    [Fact]
    public async Task QueueArtifacts_ForAiStory_RequiresOutlineApproved()
    {
        var store = Seed();
        var story = store.Items<Story>().Single();
        story.Source = StorySource.Ai;
        var v1 = store.Items<StoryVersion>().Single();
        v1.OutlineApprovedAt = null;
        v1.OutlineApprovedByUserId = null;
        story.Status = StoryStatus.OutlineReview;
        var service = BuildService(store);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.QueueArtifactsAsync(1, 1, 1, null));
    }

    [Fact]
    public async Task QueueArtifacts_ForAiStory_WithOutlineApproved_Succeeds()
    {
        var store = Seed();
        var story = store.Items<Story>().Single();
        story.Source = StorySource.Ai;
        var v1 = store.Items<StoryVersion>().Single();
        v1.OutlineApprovedAt = DateTime.UtcNow;
        v1.OutlineApprovedByUserId = 1;
        story.Status = StoryStatus.OutlineReview;
        var service = BuildService(store);

        var jobId = await service.QueueArtifactsAsync(1, 1, 1, null);
        Assert.True(jobId > 0);
    }

    private static StableVersionArtifactHandoffService BuildService(StableHandoffFakeUnitOfWork store) =>
        new(store, new StubLearningService(), new StubSafetyService());

    private static StableHandoffFakeUnitOfWork Seed()
    {
        var store = new StableHandoffFakeUnitOfWork();
        store.Seed(new UserAccount { Id = 1, Role = UserRole.Parent });
        store.Seed(new ChildProfile
        {
            Id = 1, OwnerUserId = 1, Nickname = "Mây",
            AgeBand = AgeBand.Age_6_8, Language = "vi", Status = ChildProfileStatus.Active
        });
        store.Seed(new Story
        {
            Id = 1, Title = "Mây và khu rừng",
            Content = "Lan và Minh cùng nhau vào rừng khám phá.",
            AgeBand = "6-8", Language = "vi", VocabularyLevel = "level_2",
            Source = StorySource.Manual, Status = StoryStatus.Draft,
            AuthorUserId = 1, ChildProfileId = 1
        });
        store.Seed(new StoryVersion
        {
            Id = 1, StoryId = 1, VersionNo = 1, EditType = VersionEditType.Initial,
            Title = "Mây và khu rừng",
            Content = "Lan và Minh cùng nhau vào rừng khám phá.",
            Lesson = "Tình bạn", IsCurrent = true
        });
        return store;
    }

    private sealed class StubLearningService : ILearningProfileService
    {
        public Task<LearningProfileDto> SetLearningProfileAsync(int childProfileId, int currentUserId, SetLearningProfileRequestDto request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public Task<LearningProfileDto> GetLearningProfileAsync(int childProfileId, int currentUserId, CancellationToken cancellationToken = default) =>
            throw new NotFoundException("LearningProfile");
        public Task DeleteLearningProfileAsync(int childProfileId, int currentUserId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    private sealed class StubSafetyService : ISafetyPolicyService
    {
        public Task<SafetyPolicyDto> SetSafetyPolicyAsync(int childProfileId, int currentUserId, SetSafetyPolicyRequestDto request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public Task<SafetyPolicyDto> GetSafetyPolicyAsync(int childProfileId, int currentUserId, CancellationToken cancellationToken = default) =>
            throw new NotFoundException("SafetyPolicy");
        public Task DeleteSafetyPolicyAsync(int childProfileId, int currentUserId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    private sealed class StableHandoffFakeUnitOfWork : IUnitOfWork
    {
        private readonly Dictionary<Type, object> _repositories = [];
        public IGenericRepository<T> Repository<T>() where T : class =>
            (IGenericRepository<T>)(_repositories.TryGetValue(typeof(T), out var value) ? value : _repositories[typeof(T)] = new StableHandoffFakeRepository<T>());
        public void Seed<T>(T item) where T : class => ((StableHandoffFakeRepository<T>)Repository<T>()).Items.Add(item);
        public IReadOnlyList<T> Items<T>() where T : class => ((StableHandoffFakeRepository<T>)Repository<T>()).Items;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AcquireTransactionLockAsync(int resourceId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StableHandoffFakeRepository<T> : IGenericRepository<T> where T : class
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
            if (entity is BaseEntity item && item.Id == 0)
                item.Id = Items.OfType<BaseEntity>().Select(value => value.Id).DefaultIfEmpty().Max() + 1;
            Items.Add(entity);
            return Task.FromResult(entity);
        }
        public async Task AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
        {
            foreach (var item in entities) await AddAsync(item, cancellationToken);
        }
        public void Update(T entity) { }
        public void Delete(T entity) => Items.Remove(entity);
        public void DeleteRange(IEnumerable<T> entities) { foreach (var item in entities.ToArray()) Items.Remove(item); }
        public Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(predicate is null ? Items.Count : Items.Count(predicate.Compile()));
        public Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.Any(predicate.Compile()));
        public IQueryable<T> Query() => Items.AsQueryable();
    }
}
