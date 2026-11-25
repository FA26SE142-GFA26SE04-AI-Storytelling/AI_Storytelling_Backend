using System;
using System.Threading;
using System.Threading.Tasks;

namespace StoryPlatform.Application.Abstractions.Persistence;

/// <summary>
/// Interface Unit of Work đảm bảo tính toàn vẹn dữ liệu (Transaction) và cung cấp truy cập tập trung tới các Generic Repositories.
/// </summary>
public interface IUnitOfWork : IDisposable, IAsyncDisposable
{
    /// <summary>Discard the abandoned EF graph before recovering from a rolled-back transaction.</summary>
    void DiscardTrackedChanges() { }
    /// <summary>
    /// Lấy Generic Repository cho bất kỳ Entity nào một cách tự động
    /// </summary>
    IGenericRepository<T> Repository<T>() where T : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    bool HasActiveTransaction => false;
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task AcquireTransactionLockAsync(int resourceId, CancellationToken cancellationToken = default);
    Task AcquireEventLockAsync(int eventId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    Task AcquireAccountAiActivityLockAsync(int accountUserId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);
}
