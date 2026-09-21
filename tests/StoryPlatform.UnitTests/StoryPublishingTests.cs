using System.Linq.Expressions;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.Stories.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class StoryPublishingTests
{
    [Theory]
    [InlineData(StoryStatus.Draft)]
    [InlineData(StoryStatus.ContentReview)]
    [InlineData(StoryStatus.Approved)]
    [InlineData(StoryStatus.MediaProcessing)]
    [InlineData(StoryStatus.Archived)]
    public async Task PublishStoryAsync_ThrowsConflict_WhenStatusIsNotReady(StoryStatus status)
    {
        var uow = new FakeUnitOfWork();
        uow.Seed(new Story
        {
            Id = 1,
            AuthorUserId = 1,
            Title = "Story Test",
            Status = status,
            IsPublished = false
        });
        var service = new StoryService(uow);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => service.PublishStoryAsync(1, 1));
        Assert.Contains("CANNOT_BYPASS_APPROVAL_MEDIA", ex.Message);
    }

    [Fact]
    public async Task PublishStoryAsync_Succeeds_WhenStatusIsReady()
    {
        var uow = new FakeUnitOfWork();
        uow.Seed(new Story
        {
            Id = 1,
            AuthorUserId = 1,
            Title = "Story Test Ready",
            Status = StoryStatus.Ready,
            IsPublished = false
        });
        var service = new StoryService(uow);

        var result = await service.PublishStoryAsync(1, 1);

        Assert.True(result);
        var updated = uow.Items<Story>().Single(s => s.Id == 1);
        Assert.True(updated.IsPublished);
        Assert.Equal(StoryStatus.Ready, updated.Status);
    }

    [Fact]
    public async Task PublishStoryAsync_ThrowsNotFound_WhenStoryDoesNotExist()
    {
        var uow = new FakeUnitOfWork();
        var service = new StoryService(uow);

        await Assert.ThrowsAsync<NotFoundException>(() => service.PublishStoryAsync(999, 1));
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        private readonly Dictionary<Type, object> _repositories = [];
        public IGenericRepository<T> Repository<T>() where T : class =>
            (IGenericRepository<T>)(_repositories.TryGetValue(typeof(T), out var r) ? r : _repositories[typeof(T)] = new FakeRepo<T>());
        public void Seed<T>(T item) where T : class => ((FakeRepo<T>)Repository<T>()).Items.Add(item);
        public IReadOnlyList<T> Items<T>() where T : class => ((FakeRepo<T>)Repository<T>()).Items;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AcquireTransactionLockAsync(int resourceId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeRepo<T> : IGenericRepository<T> where T : class
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
            Items.Add(entity);
            return Task.FromResult(entity);
        }
        public Task AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Update(T entity) { }
        public void Delete(T entity) => Items.Remove(entity);
        public void DeleteRange(IEnumerable<T> entities) => Items.Clear();
        public Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(predicate is null ? Items.Count : Items.Count(predicate.Compile()));
        public Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.Any(predicate.Compile()));
        public IQueryable<T> Query() => Items.AsQueryable();
    }
}
