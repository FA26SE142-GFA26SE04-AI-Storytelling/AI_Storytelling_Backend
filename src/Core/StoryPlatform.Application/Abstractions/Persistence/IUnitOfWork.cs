using System;
using System.Threading;
using System.Threading.Tasks;

namespace StoryPlatform.Application.Abstractions.Persistence;

/// <summary>
/// Interface Unit of Work đảm bảo tính toàn vẹn dữ liệu (Transaction) và cung cấp truy cập tập trung tới các Generic Repositories.
/// </summary>
public interface IUnitOfWork : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Lấy Generic Repository cho bất kỳ Entity nào một cách tự động
    /// </summary>
    IGenericRepository<T> Repository<T>() where T : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);
}
