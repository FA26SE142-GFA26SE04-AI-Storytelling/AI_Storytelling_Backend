using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Infrastructure.Persistence;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence.Repositories;

/// <summary>
/// Lớp triển khai Generic Repository Pattern sử dụng Entity Framework Core.
/// </summary>
/// <typeparam name="T">Kiểu thực thể dữ liệu</typeparam>
public class GenericRepository<T> : IGenericRepository<T> where T : class
{
    protected readonly ApplicationDbContext _context;
    protected readonly DbSet<T> _dbSet;

    public GenericRepository(ApplicationDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _dbSet = _context.Set<T>();
    }

    public virtual async Task<T?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
    {
        return await _dbSet.FindAsync(new[] { id }, cancellationToken);
    }

    public virtual async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet.AsNoTracking().ToListAsync(cancellationToken);
    }

    public virtual async Task<IReadOnlyList<T>> FindAsync(
        Expression<Func<T, bool>> predicate,
        string? includeProperties = null,
        CancellationToken cancellationToken = default)
    {
        IQueryable<T> query = _dbSet.AsNoTracking().Where(predicate);

        if (!string.IsNullOrWhiteSpace(includeProperties))
        {
            foreach (var includeProperty in includeProperties.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                query = query.Include(includeProperty.Trim());
            }
        }

        return await query.ToListAsync(cancellationToken);
    }

    public virtual async Task<T?> FirstOrDefaultAsync(
        Expression<Func<T, bool>> predicate,
        string? includeProperties = null,
        CancellationToken cancellationToken = default)
    {
        IQueryable<T> query = _dbSet.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(includeProperties))
        {
            foreach (var includeProperty in includeProperties.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                query = query.Include(includeProperty.Trim());
            }
        }

        return await query.FirstOrDefaultAsync(predicate, cancellationToken);
    }

    public virtual async Task<(IReadOnlyList<T> Items, int TotalCount)> GetPagedAsync(
        int pageIndex,
        int pageSize,
        Expression<Func<T, bool>>? filter = null,
        Func<IQueryable<T>, IOrderedQueryable<T>>? orderBy = null,
        string? includeProperties = null,
        CancellationToken cancellationToken = default)
    {
        IQueryable<T> query = _dbSet.AsNoTracking();

        if (filter != null)
        {
            query = query.Where(filter);
        }

        if (!string.IsNullOrWhiteSpace(includeProperties))
        {
            foreach (var includeProperty in includeProperties.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                query = query.Include(includeProperty.Trim());
            }
        }

        var totalCount = await query.CountAsync(cancellationToken);

        if (orderBy != null)
        {
            query = orderBy(query);
        }

        var items = await query.Skip((pageIndex - 1) * pageSize)
                               .Take(pageSize)
                               .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public virtual async Task<T> AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        await _dbSet.AddAsync(entity, cancellationToken);
        return entity;
    }

    public virtual async Task AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
    {
        await _dbSet.AddRangeAsync(entities, cancellationToken);
    }

    public virtual void Update(T entity)
    {
        if (entity is StoryVersion version)
        {
            UpdateVersion(version);
            return;
        }
        var entry = _context.Entry(entity);
        if (entry.State == EntityState.Detached)
        {
            var entityType = _context.Model.FindEntityType(typeof(T));
            var primaryKey = entityType?.FindPrimaryKey();
            if (primaryKey != null)
            {
                var keyProperties = primaryKey.Properties;
                var keyValues = keyProperties.Select(p => p.GetGetter().GetClrValue(entity)).ToArray();
                var tracked = _context.ChangeTracker.Entries<T>()
                    .FirstOrDefault(e => e.Entity != null && keyProperties.All(p => Equals(p.GetGetter().GetClrValue(e.Entity), p.GetGetter().GetClrValue(entity))));
                if (tracked != null)
                {
                    tracked.CurrentValues.SetValues(entity);
                    tracked.State = EntityState.Modified;
                    return;
                }
            }

            _dbSet.Attach(entity);
        }

        entry.State = EntityState.Modified;
    }

    private void UpdateVersion(StoryVersion version)
    {
        if (_context.Entry(version).State == EntityState.Added) return;
        var tracked = _context.StoryVersions.Local.FirstOrDefault(x => x.Id == version.Id);
        if (tracked is not null && !ReferenceEquals(tracked, version))
        {
            _context.Entry(tracked).CurrentValues.SetValues(version);
            CopyOwned(tracked.AuthoringMetadata, version.AuthoringMetadata);
            CopyOwned(tracked.Moderation, version.Moderation);
            CopyOwned(tracked.MediaState, version.MediaState);
            _context.Entry(tracked).State = EntityState.Modified;
            return;
        }
        version.AuthoringMetadata.StoryVersionId = version.Id;
        version.AuthoringMetadata.StoryId = version.StoryId;
        version.Moderation.StoryVersionId = version.Id;
        version.MediaState.StoryVersionId = version.Id;
        if (_context.Entry(version).State == EntityState.Detached) _context.Attach(version);
        _context.Entry(version).State = EntityState.Modified;
        _context.Entry(version.AuthoringMetadata).State = EntityState.Modified;
        _context.Entry(version.Moderation).State = EntityState.Modified;
        _context.Entry(version.MediaState).State = EntityState.Modified;
    }

    private void CopyOwned<TOwned>(TOwned target, TOwned source) where TOwned : class
    {
        // Never overwrite an owned primary/FK key from a default-constructed input object.
        var entry = _context.Entry(target);
        foreach (var property in entry.Properties.Where(x => !x.Metadata.IsPrimaryKey() &&
            !x.Metadata.GetContainingForeignKeys().Any(fk => fk.IsOwnership)))
            property.CurrentValue = _context.Entry(source).Property(property.Metadata.Name).CurrentValue;
        entry.State = EntityState.Modified;
    }

    public virtual void Delete(T entity)
    {
        if (_context.Entry(entity).State == EntityState.Detached)
        {
            var entityType = _context.Model.FindEntityType(typeof(T));
            var primaryKey = entityType?.FindPrimaryKey();
            if (primaryKey != null)
            {
                var keyProperties = primaryKey.Properties;
                var keyValues = keyProperties.Select(p => p.GetGetter().GetClrValue(entity)).ToArray();
                var tracked = _context.ChangeTracker.Entries<T>()
                    .FirstOrDefault(e => e.Entity != null && keyProperties.All(p => Equals(p.GetGetter().GetClrValue(e.Entity), p.GetGetter().GetClrValue(entity))));
                if (tracked != null)
                {
                    _dbSet.Remove(tracked.Entity);
                    return;
                }
            }

            _dbSet.Attach(entity);
        }

        _dbSet.Remove(entity);
    }

    public virtual void DeleteRange(IEnumerable<T> entities)
    {
        _dbSet.RemoveRange(entities);
    }

    public virtual async Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default)
    {
        if (predicate == null)
        {
            return await _dbSet.CountAsync(cancellationToken);
        }

        return await _dbSet.CountAsync(predicate, cancellationToken);
    }

    public virtual async Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return await _dbSet.AnyAsync(predicate, cancellationToken);
    }

    public virtual IQueryable<T> Query()
    {
        return _dbSet.AsQueryable();
    }
}
