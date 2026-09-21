using System.Linq.Expressions;
using StoryPlatform.Application.Abstractions.AI;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Learning.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.Learning.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Safety.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.Safety.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;
using StoryPlatform.Application.Features.ExistingStories.DTOs;
using StoryPlatform.Application.Features.ExistingStories.Interfaces;
using StoryPlatform.Application.Features.ExistingStories.Services;
using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Contracts.AI.Responses;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class ExistingStoryServiceTests
{
    #region Import tests

    [Fact]
    public async Task Import_CreatesStoryAndInitialVersion_WithinTransaction()
    {
        var store = SeedActiveChild();
        var service = BuildService(store);

        var result = await service.ImportAsync(1, ValidImport(), CancellationToken.None);

        Assert.Single(store.Items<Story>());
        Assert.NotEqual(0, store.Items<Story>().Single().Id); // Id được tự sinh
        Assert.Equal(StorySource.Manual, store.Items<Story>().Single().Source);
        Assert.Equal(StoryStatus.Draft, store.Items<Story>().Single().Status);
        Assert.Single(store.Items<StoryVersion>(), v => v.EditType == VersionEditType.Initial);
        Assert.True(store.Items<StoryVersion>().Single(v => v.EditType == VersionEditType.Initial).IsCurrent);
        Assert.Equal("Draft", result.StoryStatus);
        Assert.Equal("Initial", result.EditType);
    }

    [Fact]
    public async Task Import_RejectsBlankContent()
    {
        var store = SeedActiveChild();
        var service = BuildService(store);
        var req = ValidImport();
        req.Content = "   ";

        await Assert.ThrowsAsync<BadRequestException>(() => service.ImportAsync(1, req, CancellationToken.None));
        Assert.Empty(store.Items<Story>());
    }

    [Fact]
    public async Task Import_RejectsUnsupportedInputMethod()
    {
        var store = SeedActiveChild();
        var service = BuildService(store);
        var req = ValidImport();
        req.InputMethod = "docx";

        await Assert.ThrowsAsync<BadRequestException>(() => service.ImportAsync(1, req, CancellationToken.None));
    }

    [Fact]
    public async Task Import_RejectsOversizedContent()
    {
        var store = SeedActiveChild();
        var service = BuildService(store);
        var req = ValidImport();
        req.Content = new string('a', 200_001);

        await Assert.ThrowsAsync<BadRequestException>(() => service.ImportAsync(1, req, CancellationToken.None));
    }

    [Fact]
    public async Task Import_NormalizesWhitespace()
    {
        var store = SeedActiveChild();
        var service = BuildService(store);
        var req = ValidImport();
        req.Content = "Lan  và   Minh\r\n\r\n\r\n\r\ncùng chia sẻ.";

        var result = await service.ImportAsync(1, req, CancellationToken.None);
        var version = store.Items<StoryVersion>().Single();
        Assert.DoesNotContain("   ", version.Content);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public async Task Import_IdempotentByKey_ReturnsExistingStory()
    {
        var store = SeedActiveChild();
        var service = BuildService(store);
        var req = ValidImport();
        req.IdempotencyKey = "fixed-key-001";

        var first = await service.ImportAsync(1, req, CancellationToken.None);
        var second = await service.ImportAsync(1, req, CancellationToken.None);

        Assert.Equal(first.StoryId, second.StoryId);
        Assert.Equal(first.StoryVersionId, second.StoryVersionId);
        Assert.Single(store.Items<Story>());
    }

    [Fact]
    public async Task Import_RejectsNonActiveChild()
    {
        var store = SeedActiveChild();
        store.Seed(new ChildProfile
        {
            Id = 2,
            OwnerUserId = 1,
            Nickname = "Mây 2",
            AgeBand = AgeBand.Age_6_8,
            Language = "vi",
            Status = ChildProfileStatus.Suspended
        });
        var service = BuildService(store);

        var req = ValidImport();
        req.ChildProfileId = 2;

        await Assert.ThrowsAsync<BadRequestException>(() => service.ImportAsync(1, req, CancellationToken.None));
    }

    [Fact]
    public async Task Import_RequiresGenerateStoryPermission()
    {
        var store = SeedActiveChild();
        // Không có supervision -> guard sẽ throw ForbiddenException.
        var allRelationships = store.Items<SupervisionRelationship>().ToList();
        foreach (var r in allRelationships)
        {
            ((ExistingStoryFakeRepository<SupervisionRelationship>)store.Repository<SupervisionRelationship>()).RemoveDirect(r);
        }
        var service = BuildService(store);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.ImportAsync(1, ValidImport(), CancellationToken.None));
    }

    #endregion

    #region Adapt tests

    [Fact]
    public async Task Adapt_CreatesNewAiRefinedVersion_AndPreservesOriginal()
    {
        var store = SeedStoryWithV1();
        var ai = new FakeAIClientForExisting();
        var service = BuildService(store, ai);

        var result = await service.AdaptAsync(1,
            new AdaptExistingStoryRequestDto
            {
                StoryId = 1,
                BaseStoryVersionId = 1,
                Guideline = "Đơn giản hoá"
            }, CancellationToken.None);

        Assert.Equal(2, result.StoryVersionId);
        Assert.Equal("AiRefined", result.EditType);
        Assert.Equal(2, store.Items<StoryVersion>().Count());
        Assert.True(store.Items<StoryVersion>().Single(v => v.Id == 2).IsCurrent);
        Assert.False(store.Items<StoryVersion>().Single(v => v.Id == 1).IsCurrent);
        Assert.Equal("Initial", store.Items<StoryVersion>().Single(v => v.Id == 1).EditType.ToString());
    }

    [Fact]
    public async Task Adapt_RejectsStaleBaseVersion()
    {
        var store = SeedStoryWithV1();
        // v1 ban đầu IsCurrent=true; thêm v2 là current -> v1 trở thành không-current.
        var v1 = store.Items<StoryVersion>().Single(v => v.Id == 1);
        v1.IsCurrent = false;
        store.Seed(new StoryVersion
        {
            Id = 2,
            StoryId = 1,
            VersionNo = 2,
            EditType = VersionEditType.HumanEdited,
            Title = "Old",
            Content = "Old",
            Lesson = "Old",
            IsCurrent = true
        });
        var ai = new FakeAIClientForExisting();
        var service = BuildService(store, ai);

        await Assert.ThrowsAsync<ConflictException>(() => service.AdaptAsync(1,
            new AdaptExistingStoryRequestDto { StoryId = 1, BaseStoryVersionId = 1 },
            CancellationToken.None));
    }

    [Fact]
    public async Task Adapt_RejectsNonExistingStorySource()
    {
        var store = SeedStoryWithV1();
        var aiStory = store.Items<Story>().Single();
        aiStory.Source = StorySource.Ai;
        var service = BuildService(store);

        await Assert.ThrowsAsync<BadRequestException>(() => service.AdaptAsync(1,
            new AdaptExistingStoryRequestDto { StoryId = 1, BaseStoryVersionId = 1 },
            CancellationToken.None));
    }

    #endregion

    #region Manual Edit tests

    [Fact]
    public async Task UpdateContent_CreatesNewHumanEditedVersion()
    {
        var store = SeedStoryWithV1();
        var service = BuildService(store);

        var result = await service.UpdateContentAsync(1,
            new ManualEditRequestDto
            {
                StoryId = 1,
                BaseStoryVersionId = 1,
                Title = "Tiêu đề mới",
                Content = "Nội dung mới",
                Lesson = "Bài học mới"
            }, CancellationToken.None);

        Assert.Equal(2, result.StoryVersionId);
        Assert.Equal("HumanEdited", result.EditType);
        Assert.True(store.Items<StoryVersion>().Single(v => v.Id == 2).IsCurrent);
        Assert.False(store.Items<StoryVersion>().Single(v => v.Id == 1).IsCurrent);
        Assert.Equal("Nội dung mới", store.Items<StoryVersion>().Single(v => v.Id == 2).Content);
    }

    [Fact]
    public async Task UpdateContent_NeverOverwritesOriginalVersion()
    {
        var store = SeedStoryWithV1();
        var service = BuildService(store);
        var originalContent = store.Items<StoryVersion>().Single(v => v.Id == 1).Content;

        await service.UpdateContentAsync(1,
            new ManualEditRequestDto
            {
                StoryId = 1,
                BaseStoryVersionId = 1,
                Title = "Mới",
                Content = "Hoàn toàn mới",
                Lesson = "Mới"
            }, CancellationToken.None);

        var v1 = store.Items<StoryVersion>().Single(v => v.Id == 1);
        Assert.Equal(originalContent, v1.Content);
    }

    #endregion

    #region Keep Original tests

    [Fact]
    public async Task KeepOriginal_RequiresReason()
    {
        var store = SeedStoryWithV1();
        var service = BuildService(store);

        await Assert.ThrowsAsync<BadRequestException>(() => service.KeepOriginalAsync(1,
            new KeepOriginalRequestDto { StoryId = 1, StoryVersionId = 1, OverrideReason = " " },
            CancellationToken.None));
    }

    [Fact]
    public async Task KeepOriginal_ReturnsSameVersion()
    {
        var store = SeedStoryWithV1();
        var service = BuildService(store);

        var result = await service.KeepOriginalAsync(1,
            new KeepOriginalRequestDto { StoryId = 1, StoryVersionId = 1, OverrideReason = "Cố ý giữ." },
            CancellationToken.None);

        Assert.Equal(1, result.StoryVersionId);
        Assert.Equal("Initial", result.EditType);
        Assert.Equal("KeepOriginal", result.Decision);
    }

    #endregion

    #region Archive tests

    [Fact]
    public async Task Archive_SetsArchivedStatus()
    {
        var store = SeedStoryWithV1();
        var service = BuildService(store);

        var result = await service.ArchiveAsync(1,
            new ArchiveExistingStoryRequestDto { StoryId = 1, Reason = "Sai policy." },
            CancellationToken.None);

        Assert.True(result);
        Assert.Equal(StoryStatus.Archived, store.Items<Story>().Single().Status);
        Assert.Equal(ArchivedReason.SafetyConcern, store.Items<Story>().Single().ArchivedReason);
    }

    #endregion

    #region Fixtures

    private static ExistingStoryService BuildService(ExistingStoryFakeUnitOfWork store, FakeAIClientForExisting? ai = null) =>
        new(store,
            new PassThroughAccessGuard(store),
            ai ?? new FakeAIClientForExisting(),
            new NoopHandoffService(),
            new RecordingAuditLogWriter());

    private static ImportStoryRequestDto ValidImport() => new()
    {
        ChildProfileId = 1,
        InputMethod = "paste",
        Title = "Mây và khu rừng",
        Content = "Lan và Minh cùng nhau vào rừng khám phá.",
        Language = "vi"
    };

    private static ExistingStoryFakeUnitOfWork SeedActiveChild()
    {
        var store = new ExistingStoryFakeUnitOfWork();
        store.Seed(new UserAccount { Id = 1, Role = UserRole.Parent, Status = AccountStatus.LoggedIn });
        store.Seed(new ChildProfile
        {
            Id = 1,
            OwnerUserId = 1,
            Nickname = "Mây",
            AgeBand = AgeBand.Age_6_8,
            Language = "vi",
            Status = ChildProfileStatus.Active
        });
        store.Seed(new SupervisionRelationship
        {
            Id = 1,
            ChildProfileId = 1,
            SupervisorUserId = 1,
            SupervisorRole = SupervisorRole.Owner,
            CreatedAt = DateTime.UtcNow
        });
        store.Seed(new SupervisionPermission
        {
            Id = 1,
            SupervisionRelationshipId = 1,
            Permission = Permission.GenerateStory
        });
        return store;
    }

    private static ExistingStoryFakeUnitOfWork SeedStoryWithV1()
    {
        var store = SeedActiveChild();
        store.Seed(new Story
        {
            Id = 1,
            Title = "Mây và khu rừng",
            Content = "Lan và Minh cùng nhau vào rừng khám phá.",
            AgeBand = "6-8",
            Language = "vi",
            Source = StorySource.Manual,
            Status = StoryStatus.Draft,
            AuthorUserId = 1,
            ChildProfileId = 1
        });
        store.Seed(new StoryVersion
        {
            Id = 1,
            StoryId = 1,
            VersionNo = 1,
            EditType = VersionEditType.Initial,
            Title = "Mây và khu rừng",
            Content = "Lan và Minh cùng nhau vào rừng khám phá.",
            Lesson = "Tình bạn",
            IsCurrent = true
        });
        return store;
    }

    private sealed class PassThroughAccessGuard(ExistingStoryFakeUnitOfWork store) : ISupervisionAccessGuard
    {
        public Task<SupervisionRelationship> EnsureActiveSupervisionAsync(int childProfileId, int userId, CancellationToken cancellationToken = default)
        {
            var rel = store.Items<SupervisionRelationship>().FirstOrDefault(r =>
                r.ChildProfileId == childProfileId && r.SupervisorUserId == userId && r.RevokedAt == null)
                ?? throw new ForbiddenException();
            return Task.FromResult(rel);
        }

        public Task EnsureOwnerAsync(int childProfileId, int userId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task EnsurePermissionAsync(int childProfileId, int userId, Permission permission, CancellationToken cancellationToken = default)
        {
            var rel = store.Items<SupervisionRelationship>().FirstOrDefault(r =>
                r.ChildProfileId == childProfileId && r.SupervisorUserId == userId && r.RevokedAt == null)
                ?? throw new ForbiddenException();
            if (rel.SupervisorRole == SupervisorRole.Owner) return Task.CompletedTask;
            var ok = store.Items<SupervisionPermission>().Any(p =>
                p.SupervisionRelationshipId == rel.Id && p.Permission == permission);
            if (!ok) throw new ForbiddenException();
            return Task.CompletedTask;
        }
    }

    private sealed class NoopHandoffService : IStableVersionArtifactHandoffService
    {
        public List<(int, int)> Calls { get; } = [];
        public Task<int> QueueArtifactsAsync(int storyId, int storyVersionId, int requestedByUserId, int? generationRequestId, CancellationToken cancellationToken = default)
        {
            Calls.Add((storyId, storyVersionId));
            return Task.FromResult(0);
        }
    }

    private sealed class RecordingAuditLogWriter : IAuditLogWriter
    {
        public List<string> Actions { get; } = [];
        public Task LogAsync(int? actorUserId, string action, string entityType, int entityId, object? beforeState, object? afterState, CancellationToken cancellationToken = default)
        {
            Actions.Add($"{action}:{entityType}#{entityId}");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAIClientForExisting : IAIStoryGenerationClient
    {
        public List<string> Calls { get; } = [];
        public Task<RefineStoryContentResponse> RefineStoryContentAsync(RefineStoryContentRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add("refine");
            return Task.FromResult(new RefineStoryContentResponse
            {
                RequestId = request.RequestId,
                Story = new StoryContentDto
                {
                    Title = "Lan và Minh (đã chỉnh)",
                    Lesson = "Chia sẻ là đẹp",
                    StorySections = [new StorySectionDto(1, "", "Lan và Minh cùng chia sẻ sách trong rừng.")]
                }
            });
        }

        public Task<GenerateOutlineResponse> GenerateOutlineAsync(GenerateOutlineRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GenerateStoryResponse> GenerateStoryAsync(GenerateStoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RefineStoryResponse> RefineStoryAsync(RefineStoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EvaluateStoryResponse> EvaluateStoryAsync(EvaluateStoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ExistingStoryFakeUnitOfWork : IUnitOfWork
    {
        private readonly Dictionary<Type, object> _repositories = [];

        public IGenericRepository<T> Repository<T>() where T : class =>
            (IGenericRepository<T>)(_repositories.TryGetValue(typeof(T), out var value) ? value : _repositories[typeof(T)] = new ExistingStoryFakeRepository<T>());

        public void Seed<T>(T item) where T : class => ((ExistingStoryFakeRepository<T>)Repository<T>()).Items.Add(item);
        public IReadOnlyList<T> Items<T>() where T : class => ((ExistingStoryFakeRepository<T>)Repository<T>()).Items;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AcquireTransactionLockAsync(int resourceId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ExistingStoryFakeRepository<T> : IGenericRepository<T> where T : class
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
        public void DeleteRange(IEnumerable<T> entities)
        {
            foreach (var item in entities.ToArray()) Items.Remove(item);
        }
        public Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(predicate is null ? Items.Count : Items.Count(predicate.Compile()));
        public Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.Any(predicate.Compile()));
        public IQueryable<T> Query() => Items.AsQueryable();
        public void RemoveDirect(T entity) => Items.Remove(entity);
    }

    #endregion
}
