namespace StoryPlatform.Application.Features.ChildProfiles.ReadingSlots;

/// <summary>
/// Chọn hồ sơ trẻ được tiếp tục đọc khi số hồ sơ Active vượt số hồ sơ tối đa của gói (thường là lúc về Miễn phí).
/// </summary>
public interface IReadingSlotService
{
    Task<ReadingSlotsDto> GetAsync(int ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>Đổi lựa chọn; tối đa 1 lần mỗi tuần (giờ Việt Nam). Trẻ của hồ sơ chuyển sang chỉ xem bị đăng xuất.</summary>
    Task<ReadingSlotsDto> SelectAsync(int ownerUserId, IReadOnlyList<int> childProfileIds, CancellationToken cancellationToken = default);

    Task<IReadOnlySet<int>> GetReadableProfileIdsAsync(int ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>Ném ForbiddenException khi hồ sơ đang chỉ xem.</summary>
    Task EnsureCanReadAsync(int childProfileId, CancellationToken cancellationToken = default);
}
