namespace StoryPlatform.Application.Features.PlanBenefits.Interfaces;

/// <summary>Đếm mức đã dùng của các quyền lợi có giới hạn theo tuần, ngày hoặc chu kỳ.</summary>
public interface IPlanUsageService
{
    /// <summary>
    /// Số truyện thư viện công khai (truyện Creator) mà các hồ sơ trẻ của tài khoản mở lần đầu trong tuần hiện tại.
    /// Đọc lại truyện đã mở trước đó không tính; phiên mở qua gói truyện gia đình không tính.
    /// </summary>
    Task<int> CountNewLibraryStoriesThisWeekAsync(int ownerUserId, DateTime utcNow, CancellationToken cancellationToken = default);

    /// <summary>Đếm truyện thư viện mở lần đầu từ đầu kỳ đến trước mốc kết thúc kỳ.</summary>
    Task<int> CountNewLibraryStoriesInPeriodAsync(int ownerUserId, DateTime periodStart, DateTime periodEnd, CancellationToken cancellationToken = default);

    /// <summary>Danh sách StoryId thư viện công khai mà các hồ sơ trẻ của tài khoản mở lần đầu trong tuần hiện tại.</summary>
    Task<IReadOnlyList<int>> ListNewLibraryStoryIdsThisWeekAsync(int ownerUserId, DateTime utcNow, CancellationToken cancellationToken = default);

    /// <summary>Một hồ sơ bất kỳ của tài khoản đã từng mở truyện thư viện này.</summary>
    Task<bool> HasOpenedLibraryStoryAsync(int ownerUserId, int storyId, CancellationToken cancellationToken = default);

    /// <summary>Số lần trợ lý viết (task mới) tài khoản đã tạo trong ngày hiện tại.</summary>
    Task<int> CountWritingAssistsTodayAsync(int userId, DateTime utcNow, CancellationToken cancellationToken = default);
}
