using System.Linq.Expressions;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.UnitTests.Features.TokenQuota;

/// <summary>
/// Unit of work chạy trên list trong bộ nhớ cho test sổ lượt và chu kỳ gói. Ghi lại thứ tự các lời gọi
/// transaction / khoá để test kiểm tra được.
/// </summary>
internal sealed class InMemoryUnitOfWork : IUnitOfWork
{
    private readonly Dictionary<Type, object> _repositories = [];

    public List<string> Calls { get; } = [];

    public IGenericRepository<T> Repository<T>() where T : class =>
        (IGenericRepository<T>)(_repositories.TryGetValue(typeof(T), out var repository)
            ? repository
            : _repositories[typeof(T)] = new InMemoryRepository<T>());

    public T Seed<T>(T item) where T : class
    {
        Repository<T>().AddAsync(item).GetAwaiter().GetResult();
        return item;
    }

    public IReadOnlyList<T> Items<T>() where T : class => ((InMemoryRepository<T>)Repository<T>()).Items;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);

    public Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("begin");
        return Task.CompletedTask;
    }

    public Task AcquireTransactionLockAsync(int resourceId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"lock:{resourceId}");
        return Task.CompletedTask;
    }

    public Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("commit");
        return Task.CompletedTask;
    }

    public Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("rollback");
        return Task.CompletedTask;
    }

    public void Dispose() { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private sealed class InMemoryRepository<T> : IGenericRepository<T> where T : class
    {
        public List<T> Items { get; } = [];

        public Task<T?> GetByIdAsync(object id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(item => item is BaseEntity entity && entity.Id == Convert.ToInt32(id)));

        public Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<T>>(Visible().ToArray());

        public Task<IReadOnlyList<T>> FindAsync(
            Expression<Func<T, bool>> predicate, string? includeProperties = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<T>>(Visible().Where(predicate.Compile()).ToArray());

        public Task<T?> FirstOrDefaultAsync(
            Expression<Func<T, bool>> predicate, string? includeProperties = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Visible().FirstOrDefault(predicate.Compile()));

        public Task<(IReadOnlyList<T> Items, int TotalCount)> GetPagedAsync(
            int pageIndex, int pageSize, Expression<Func<T, bool>>? filter = null,
            Func<IQueryable<T>, IOrderedQueryable<T>>? orderBy = null, string? includeProperties = null,
            CancellationToken cancellationToken = default)
        {
            var query = Visible().AsQueryable();
            if (filter != null) query = query.Where(filter);
            var total = query.Count();
            if (orderBy != null) query = orderBy(query);
            IReadOnlyList<T> page = query.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToArray();
            return Task.FromResult((page, total));
        }

        public Task<T> AddAsync(T entity, CancellationToken cancellationToken = default)
        {
            if (entity is BaseEntity added && added.Id == 0)
            {
                added.Id = Items.OfType<BaseEntity>().Select(item => item.Id).DefaultIfEmpty().Max() + 1;
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
        public void DeleteRange(IEnumerable<T> entities) => Items.RemoveAll(entities.Contains);

        public Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(predicate is null ? Visible().Count() : Visible().Count(predicate.Compile()));

        public Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) =>
            Task.FromResult(Visible().Any(predicate.Compile()));

        public IQueryable<T> Query() => Visible().AsQueryable();

        // Giống global query filter !IsDeleted của EF.
        private IEnumerable<T> Visible() => Items.Where(item => item is not BaseEntity entity || !entity.IsDeleted);
    }
}
