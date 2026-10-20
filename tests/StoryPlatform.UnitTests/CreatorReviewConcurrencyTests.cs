using System.Linq.Expressions;
using System.Text.Json;
using Moq;
using StoryPlatform.Application.Abstractions.AI;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AIStoryInput.Models;
using StoryPlatform.Application.Features.StoryReview.DTOs;
using StoryPlatform.Application.Features.StoryReview.Services;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Contracts.AI.Responses;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests;

/// <summary>Deterministic overlapping requests, independent tracking caches and a real shared semaphore.
/// Persistence/provider are simulated; this does not prove PostgreSQL advisory-lock behavior.</summary>
public sealed class CreatorReviewConcurrencyTests
{
    [Fact]
    public async Task ConfirmWins_EditWaitingForSafetyCannotReplaceConfirmedVersion()
    {
        var db = Seed(); var gate = new SafetyGate();
        await using var edit = new Session(db); await using var confirm = new Session(db);
        var pending = Service(edit, gate).UpdateStoryAsync(1, 1, Input(db));
        await Wait(gate.Entered.Task);
        Assert.Equal(0, edit.BeginCount); // never hold a transaction over the provider call
        await Service(confirm).ConfirmCreatorContentAsync(1, 1, new() { VersionId = 1 }).WaitAsync(TimeSpan.FromSeconds(10));
        gate.Complete();
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => pending.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(409, conflict.StatusCode);
        Assert.Contains("INVALID_STORY_STATUS", conflict.Message);
        Assert.Single(db.Read<StoryVersion>());
        Assert.Equal(StoryStatus.MediaProcessing, db.Read<Story>().Single().Status);
        var job = Assert.Single(db.Read<StoryGenerationJob>());
        Assert.Equal(1, job.StoryVersionId);
        Assert.Equal(1, job.BaseStoryVersionId);
        Assert.Equal(1, db.Read<StoryVersion>().Single(x => x.IsCurrent).Id);
    }

    [Fact]
    public async Task EditWins_ConfirmWaitingOnLockReturns409BeforeValidatingNewVersion()
    {
        var db = Seed(); var gate = new SafetyGate();
        await using var edit = new Session(db) { ContinueAfterLock = Signal() };
        await using var confirm = new Session(db);
        var pendingEdit = Service(edit, gate).UpdateStoryAsync(1, 1, Input(db));
        await Wait(gate.Entered.Task); gate.Complete();
        await Wait(edit.LockAcquired.Task);
        var pendingConfirm = Service(confirm).ConfirmCreatorContentAsync(1, 1, new() { VersionId = 1 });
        await Wait(confirm.LockRequested.Task);
        edit.ContinueAfterLock.SetResult(true);
        var version = await pendingEdit.WaitAsync(TimeSpan.FromSeconds(10));
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => pendingConfirm.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(409, conflict.StatusCode);
        Assert.Contains("STALE_STORY_VERSION", conflict.Message);
        Assert.Empty(db.Read<StoryGenerationJob>());
        Assert.Equal(version.VersionId, db.Read<StoryVersion>().Single(x => x.IsCurrent).Id);
        // The new version deliberately lacks learning artifacts. A stale confirm must be 409, not validation's 400.
        Assert.False((await Service(confirm).ValidateAsync(1, 1)).CanConfirmContent);
        await PrepareArtifacts(db, version.VersionId);
        await using var retry = new Session(db);
        await Service(retry).ConfirmCreatorContentAsync(1, 1, new() { VersionId = version.VersionId });
        Assert.Equal(version.VersionId, Assert.Single(db.Read<StoryGenerationJob>()).StoryVersionId);
    }

    [Fact]
    public async Task TwoEditsOfV1_OnlyWinnerAppendsAVersion()
    {
        var db = Seed(); var firstGate = new SafetyGate(); var secondGate = new SafetyGate();
        await using var first = new Session(db); await using var second = new Session(db);
        var a = Service(first, firstGate).UpdateStoryAsync(1, 1, Input(db));
        var b = Service(second, secondGate).UpdateStoryAsync(1, 1, Input(db));
        await Wait(Task.WhenAll(firstGate.Entered.Task, secondGate.Entered.Task));
        firstGate.Complete(); var winner = await a.WaitAsync(TimeSpan.FromSeconds(10));
        secondGate.Complete();
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => b.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(409, conflict.StatusCode);
        Assert.Contains("STALE_STORY_VERSION", conflict.Message);
        Assert.Equal(2, db.Read<StoryVersion>().Count);
        Assert.Equal(winner.VersionId, db.Read<StoryVersion>().Single(x => x.IsCurrent).Id);
        Assert.Empty(db.Read<StoryGenerationJob>());
    }

    [Fact]
    public async Task EditUsesFreshStory_NotTheTrackedHeaderLoadedBeforeSafety()
    {
        var db = Seed(); var gate = new SafetyGate();
        await using var edit = new Session(db);
        var pending = Service(edit, gate).UpdateStoryAsync(1, 1, Input(db));
        await Wait(gate.Entered.Task);
        await using (var writer = new Session(db))
        {
            await writer.BeginTransactionAsync(); await writer.AcquireTransactionLockAsync(1);
            var story = (await writer.Repository<Story>().GetByIdAsync(1))!;
            story.Description = "metadata written while safety was running";
            writer.Repository<Story>().Update(story); await writer.CommitTransactionAsync();
        }
        gate.Complete(); await pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("metadata written while safety was running", db.Read<Story>().Single().Description);
        Assert.Equal(2, db.Read<StoryVersion>().Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SafetyFailureOrCancellation_DoesNotUndoSuccessfulConfirmation(bool cancel)
    {
        var db = Seed(); var gate = new SafetyGate(); using var cts = new CancellationTokenSource();
        await using var edit = new Session(db); await using var confirm = new Session(db);
        var pending = Service(edit, gate).UpdateStoryAsync(1, 1, Input(db), cts.Token);
        await Wait(gate.Entered.Task);
        await Service(confirm).ConfirmCreatorContentAsync(1, 1, new() { VersionId = 1 }).WaitAsync(TimeSpan.FromSeconds(10));
        if (cancel) { cts.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(10))); }
        else { gate.Complete(false); await Assert.ThrowsAsync<BadRequestException>(() => pending.WaitAsync(TimeSpan.FromSeconds(10))); }
        Assert.Equal(0, edit.BeginCount);
        Assert.Equal(1, Assert.Single(db.Read<StoryGenerationJob>()).StoryVersionId);
        Assert.Equal(StoryStatus.MediaProcessing, db.Read<Story>().Single().Status);
        Assert.Single(db.Read<StoryVersion>());
    }

    [Fact]
    public async Task AccountRevokedDuringSafety_IsRecheckedWithoutTracking()
    {
        var db = Seed(); var gate = new SafetyGate(); await using var edit = new Session(db);
        var pending = Service(edit, gate).UpdateStoryAsync(1, 1, Input(db));
        await Wait(gate.Entered.Task);
        await using (var writer = new Session(db))
        {
            await writer.BeginTransactionAsync(); await writer.AcquireTransactionLockAsync(1);
            var user = (await writer.Repository<UserAccount>().GetByIdAsync(1))!;
            user.Role = UserRole.Parent; writer.Repository<UserAccount>().Update(user); await writer.CommitTransactionAsync();
        }
        gate.Complete();
        await Assert.ThrowsAsync<ForbiddenException>(() => pending.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Single(db.Read<StoryVersion>());
    }

    [Fact]
    public async Task ActiveMediaJob_PreventsEditBeforeCallingSafety()
    {
        var db = Seed(); db.Seed(new StoryGenerationJob { Id = 1, StoryId = 1, StoryVersionId = 1,
            Operation = GenerationJobOperation.GenerateMediaPackage, Status = GenerationJobStatus.Pending });
        var gate = new SafetyGate(); await using var edit = new Session(db);
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => Service(edit, gate).UpdateStoryAsync(1, 1, Input(db)));
        Assert.Equal(409, conflict.StatusCode);
        Assert.Contains("MEDIA_JOB_ACTIVE", conflict.Message);
        Assert.False(gate.Entered.Task.IsCompleted);
        Assert.Equal(0, edit.BeginCount);
    }

    private static TaskCompletionSource<bool> Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static async Task Wait(Task task) => await task.WaitAsync(TimeSpan.FromSeconds(10));
    private static StoryReviewService Service(Session session, SafetyGate? gate = null) =>
        new(session, (gate ?? new SafetyGate()).Client.Object, new InMemoryProposalCache());
    private static UpdateStoryRequestDto Input(Database db)
    {
        var version = db.Read<StoryVersion>().Single(x => x.IsCurrent);
        return new() { VersionId = version.Id, Title = version.Title, Content = version.Content + " Lan vui vẻ chia sẻ.", Lesson = version.Lesson! };
    }
    private static Database Seed()
    {
        var source = StoryReviewServiceTests.SeedContentReviewWithAllArtifacts();
        source.Items<UserAccount>().Single().Role = UserRole.ContentCreator;
        var story = source.Items<Story>().Single(); story.Scope = StoryScope.Creator; story.ChildProfileId = null;
        source.Items<StoryVersion>().Single().SafetyScore = 99m;
        source.Seed(new StoryGenerationRequest { Id = 1, StoryId = 1, SubmittedByUserId = 1,
            Status = GenerationInputStatus.InputAccepted,
            ContextSnapshotJson = JsonSerializer.Serialize(new AIStoryInputContextSnapshot(null, "6-8", 2, "level_2", "vi", 1000,
                "always_manual", [], [], [], [], SafetyScoreThreshold: 90m, AudienceScope: "Creator", CreatorUserId: 1),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)) });
        var db = new Database();
        foreach (var x in source.Items<UserAccount>()) db.Seed(x);
        foreach (var x in source.Items<Story>()) db.Seed(x);
        foreach (var x in source.Items<StoryVersion>()) db.Seed(x);
        foreach (var x in source.Items<StoryGenerationRequest>()) db.Seed(x);
        foreach (var x in source.Items<StoryVocabulary>()) db.Seed(x);
        foreach (var x in source.Items<QuizItem>()) db.Seed(x);
        foreach (var x in source.Items<DiscussionQuestion>()) db.Seed(x);
        return db;
    }
    private static async Task PrepareArtifacts(Database db, int versionId)
    {
        await using var session = new Session(db);
        await session.BeginTransactionAsync(); await session.AcquireTransactionLockAsync(1);
        foreach (var x in db.Read<StoryVocabulary>()) { x.Id = 0; x.StoryVersionId = versionId; await session.Repository<StoryVocabulary>().AddAsync(x); }
        foreach (var x in db.Read<QuizItem>()) { x.Id = 0; x.StoryVersionId = versionId; await session.Repository<QuizItem>().AddAsync(x); }
        foreach (var x in db.Read<DiscussionQuestion>()) { x.Id = 0; x.StoryVersionId = versionId; await session.Repository<DiscussionQuestion>().AddAsync(x); }
        await session.CommitTransactionAsync();
    }
    private sealed class SafetyGate
    {
        public TaskCompletionSource<bool> Entered { get; } = Signal();
        private readonly TaskCompletionSource<EvaluateContentSafetyResponse> outcome = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Mock<IAIStoryGenerationClient> Client { get; } = new(MockBehavior.Strict);
        public SafetyGate() => Client.Setup(x => x.EvaluateContentSafetyAsync(It.IsAny<EvaluateContentSafetyRequest>(), It.IsAny<CancellationToken>()))
            .Returns<EvaluateContentSafetyRequest, CancellationToken>((_, ct) => { Entered.TrySetResult(true); return outcome.Task.WaitAsync(ct); });
        public void Complete(bool allowed = true) => outcome.SetResult(new() { IsAllowed = allowed, SafetyScore = allowed ? 99 : 20 });
    }

    private static object Copy(object value) => JsonSerializer.Deserialize(JsonSerializer.Serialize(value, value.GetType()), value.GetType())!;
    private sealed class Database
    {
        public Dictionary<Type, List<object>> Data { get; set; } = [];
        public SemaphoreSlim StoryLock { get; } = new(1, 1);
        public void Seed<T>(T row) where T : class
        {
            if (!Data.TryGetValue(typeof(T), out var list)) Data[typeof(T)] = list = [];
            list.Add(Copy(row));
        }
        public IReadOnlyList<T> Read<T>() where T : class => Data.TryGetValue(typeof(T), out var list) ? list.Select(x => (T)Copy(x)).ToArray() : [];
    }
    private sealed class Session(Database db) : IUnitOfWork
    {
        private readonly Dictionary<Type, object> repositories = [];
        private readonly Dictionary<(Type, int), object> tracking = [];
        private Dictionary<Type, List<object>>? transaction;
        private bool held;
        public int BeginCount { get; private set; }
        public TaskCompletionSource<bool> LockRequested { get; } = Signal();
        public TaskCompletionSource<bool> LockAcquired { get; } = Signal();
        public TaskCompletionSource<bool>? ContinueAfterLock { get; init; }
        public IGenericRepository<T> Repository<T>() where T : class => (IGenericRepository<T>)(repositories.TryGetValue(typeof(T), out var value)
            ? value : repositories[typeof(T)] = new SessionRepository<T>(this));
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); BeginCount++; return Task.CompletedTask; }
        public async Task AcquireTransactionLockAsync(int resourceId, CancellationToken cancellationToken = default)
        {
            Assert.Equal(1, resourceId); LockRequested.TrySetResult(true);
            await db.StoryLock.WaitAsync(cancellationToken); held = true;
            transaction = db.Data.ToDictionary(x => x.Key, x => x.Value.Select(Copy).ToList());
            LockAcquired.TrySetResult(true);
            if (ContinueAfterLock != null) await ContinueAfterLock.Task.WaitAsync(cancellationToken);
        }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(0); }
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            db.Data = transaction ?? throw new InvalidOperationException("No transaction"); Release(); return Task.CompletedTask;
        }
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) { Release(); return Task.CompletedTask; }
        private void Release() { transaction = null; if (held) { held = false; db.StoryLock.Release(); } }
        public void Dispose() => Release();
        public ValueTask DisposeAsync() { Release(); return ValueTask.CompletedTask; }
        private List<object> Rows<T>() where T : class
        {
            var data = transaction ?? db.Data;
            if (!data.TryGetValue(typeof(T), out var rows))
            {
                rows = [];
                if (transaction != null) data[typeof(T)] = rows;
            }
            return rows;
        }
        private sealed class SessionRepository<T>(Session session) : IGenericRepository<T> where T : class
        {
            private IEnumerable<T> Read() => session.Rows<T>().Select(x => (T)Copy(x));
            public Task<T?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
            {
                var key = (typeof(T), Convert.ToInt32(id));
                if (session.tracking.TryGetValue(key, out var value)) return Task.FromResult<T?>((T)value);
                var found = Read().FirstOrDefault(x => ((BaseEntity)(object)x).Id == key.Item2);
                if (found != null) session.tracking[key] = found;
                return Task.FromResult(found);
            }
            public Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, string? includeProperties = null, CancellationToken cancellationToken = default) => Task.FromResult(Read().FirstOrDefault(predicate.Compile()));
            public Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate, string? includeProperties = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<T>>(Read().Where(predicate.Compile()).ToArray());
            public Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<T>>(Read().ToArray());
            public Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) => Task.FromResult(Read().Any(predicate.Compile()));
            public Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default) => Task.FromResult(predicate == null ? Read().Count() : Read().Count(predicate.Compile()));
            public Task<T> AddAsync(T entity, CancellationToken cancellationToken = default)
            {
                Assert.True(session.held, "Writes require the story lock");
                var rows = session.Rows<T>(); var row = (BaseEntity)(object)entity;
                if (row.Id == 0) row.Id = rows.Cast<BaseEntity>().Select(x => x.Id).DefaultIfEmpty().Max() + 1;
                if (entity is StoryVersion version && version.IsCurrent)
                    Assert.DoesNotContain(rows.Cast<StoryVersion>(), x => x.StoryId == version.StoryId && x.IsCurrent);
                rows.Add(Copy(entity)); return Task.FromResult(entity);
            }
            public void Update(T entity)
            {
                Assert.True(session.held, "Writes require the story lock");
                var rows = session.Rows<T>(); var id = ((BaseEntity)(object)entity).Id;
                rows[rows.FindIndex(x => ((BaseEntity)x).Id == id)] = Copy(entity);
            }
            public async Task AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default) { foreach (var x in entities) await AddAsync(x, cancellationToken); }
            public void Delete(T entity) => session.Rows<T>().RemoveAll(x => ((BaseEntity)x).Id == ((BaseEntity)(object)entity).Id);
            public void DeleteRange(IEnumerable<T> entities) { foreach (var x in entities) Delete(x); }
            public IQueryable<T> Query() => Read().AsQueryable();
            public Task<(IReadOnlyList<T> Items, int TotalCount)> GetPagedAsync(int pageIndex, int pageSize, Expression<Func<T, bool>>? filter = null, Func<IQueryable<T>, IOrderedQueryable<T>>? orderBy = null, string? includeProperties = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        }
    }
}
