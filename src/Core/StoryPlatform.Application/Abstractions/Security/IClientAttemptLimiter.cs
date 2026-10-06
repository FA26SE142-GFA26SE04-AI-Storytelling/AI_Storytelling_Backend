namespace StoryPlatform.Application.Abstractions.Security;

/// <summary>
/// Giới hạn số lần thử sai liên tiếp theo từng thiết bị (khóa theo IP client) — Luồng 1, Bước 1.10.
/// </summary>
public interface IClientAttemptLimiter
{
    bool IsBlocked(string clientKey);

    void RegisterFailure(string clientKey);

    void Reset(string clientKey);
}
