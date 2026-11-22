namespace StoryPlatform.Application.Abstractions.Caching;

/// <summary>
/// Cache dùng chung giữa các instance (Redis). Chỉ là lớp tăng tốc best-effort: khi cache lỗi, bản cài đặt
/// phải gọi thẳng <c>factory</c> chứ không ném lỗi, nên service không bao giờ phụ thuộc vào cache để đúng.
/// </summary>
public interface IAppCache
{
    Task<T> GetOrSetAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan ttl,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>Cho phép service nhận <c>IAppCache?</c> (null khi chưa cấu hình Redis) mà không phải tự kiểm null.</summary>
public static class AppCacheExtensions
{
    public static Task<T> GetOrSetOrLoadAsync<T>(this IAppCache? cache, string key,
        Func<CancellationToken, Task<T>> factory, TimeSpan ttl, CancellationToken cancellationToken = default) =>
        cache is null ? factory(cancellationToken) : cache.GetOrSetAsync(key, factory, ttl, cancellationToken);

    public static Task RemoveIfConfiguredAsync(this IAppCache? cache, string key,
        CancellationToken cancellationToken = default) =>
        cache is null ? Task.CompletedTask : cache.RemoveAsync(key, cancellationToken);
}
