using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Infrastructure.Persistence;

namespace StoryPlatform.Infrastructure.Persistence.Repositories;

/// <summary>
/// Triển khai Unit of Work Pattern quản lý DbContext và vòng đời Transactions.
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    public void DiscardTrackedChanges() => _context.ChangeTracker.Clear();
    private readonly ApplicationDbContext _context;
    private readonly ConcurrentDictionary<Type, object> _repositories = new();
    private IDbContextTransaction? _currentTransaction;
    private bool _disposed;

    public UnitOfWork(ApplicationDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public IGenericRepository<T> Repository<T>() where T : class
    {
        return (IGenericRepository<T>)_repositories.GetOrAdd(typeof(T), _ => new GenericRepository<T>(_context));
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        SaveTranslatingConcurrencyAsync(cancellationToken);

    private async Task<int> SaveTranslatingConcurrencyAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(
                "Dữ liệu vừa được người khác thay đổi. Vui lòng tải lại và thử lại.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException
            { SqlState: "23505", ConstraintName: "UX_moderation_rubric_version" or "UX_moderation_rubric_active" })
        {
            throw new ConflictException("RUBRIC_VERSION_CONFLICT");
        }
    }

    public bool HasActiveTransaction => _currentTransaction is not null;

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_currentTransaction != null)
        {
            return;
        }
        _currentTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);
    }

    internal static long StoryLockKey(int resourceId) => 0x53544F5200000000L | (uint)resourceId;

    internal static long EventLockKey(int eventId) => 0x45564E5400000000L | (uint)eventId;

    public async Task AcquireTransactionLockAsync(int resourceId, CancellationToken cancellationToken = default)
    {
        if (_currentTransaction is null)
        {
            throw new InvalidOperationException("A transaction must be active before acquiring a workflow lock.");
        }

        var lockKey = StoryLockKey(resourceId);
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})",
            cancellationToken);
    }

    public async Task AcquireEventLockAsync(int eventId, CancellationToken cancellationToken = default)
    {
        if (_currentTransaction is null)
        {
            throw new InvalidOperationException("A transaction must be active before acquiring an event lock.");
        }

        var lockKey = EventLockKey(eventId);
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})",
            cancellationToken);
    }

    public async Task AcquireAccountAiActivityLockAsync(int accountUserId, CancellationToken cancellationToken = default)
    {
        if (_currentTransaction is null)
        {
            throw new InvalidOperationException("A transaction must be active before acquiring an account AI activity lock.");
        }

        const long accountAiActivityLockNamespace = 0x4149414300000000L;
        var lockKey = accountAiActivityLockNamespace | (uint)accountUserId;
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})",
            cancellationToken);
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await SaveTranslatingConcurrencyAsync(cancellationToken);
            if (_currentTransaction != null)
            {
                await _currentTransaction.CommitAsync(cancellationToken);
            }
        }
        catch
        {
            await RollbackTransactionAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (_currentTransaction != null)
            {
                await _currentTransaction.DisposeAsync();
                _currentTransaction = null;
            }
        }
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_currentTransaction != null)
            {
                await _currentTransaction.RollbackAsync(cancellationToken);
            }
        }
        finally
        {
            if (_currentTransaction != null)
            {
                await _currentTransaction.DisposeAsync();
                _currentTransaction = null;
            }
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _currentTransaction?.Dispose();
                _context.Dispose();
            }
            _disposed = true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_currentTransaction != null)
        {
            await _currentTransaction.DisposeAsync();
            _currentTransaction = null;
        }
        await _context.DisposeAsync();
        Dispose(false);
        GC.SuppressFinalize(this);
    }
}
