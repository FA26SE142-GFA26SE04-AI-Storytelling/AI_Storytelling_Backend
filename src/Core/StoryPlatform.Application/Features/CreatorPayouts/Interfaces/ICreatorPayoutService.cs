using StoryPlatform.Application.Features.CreatorPayouts.DTOs;

namespace StoryPlatform.Application.Features.CreatorPayouts.Interfaces;

/// <summary>
/// Chi trả cho Creator (Luồng 5, 5h). Hệ thống không chuyển tiền: Admin chuyển khoản ngoài hệ thống rồi đánh dấu Đã chi;
/// hệ thống lưu thông tin nhận tiền, số dư (sổ bút toán) và lịch sử các lần chi. Số tiền là số trước thuế.
/// </summary>
public interface ICreatorPayoutService
{
    public const long MinimumPayoutVnd = 200_000;

    /// <summary>Ngày (giờ Việt Nam) trong tháng mà kỳ của tháng trước thành chính thức, sau 7 ngày soát xét.</summary>
    public const int PeriodCloseDay = 8;

    /// <summary>Creator xem số dư, thông tin nhận tiền và lịch sử của chính mình.</summary>
    Task<CreatorPayoutOverviewDto> GetOverviewAsync(int creatorUserId, CancellationToken cancellationToken = default);

    /// <summary>Creator nhập hoặc sửa thông tin nhận tiền; mỗi lần sửa ghi audit log.</summary>
    Task<CreatorPayoutAccountDto> UpdatePayoutAccountAsync(
        int creatorUserId, UpdateCreatorPayoutAccountRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>Danh sách chi: Creator Active, có số dư từ <see cref="MinimumPayoutVnd"/> và đã có thông tin nhận tiền.</summary>
    Task<IReadOnlyList<CreatorPayoutDueDto>> GetDueAsync(CancellationToken cancellationToken = default);

    /// <summary>Admin đánh dấu Đã chi đúng bằng số dư: ghi lần chi, số dư về 0, thông báo Creator, audit log.</summary>
    Task<CreatorPayoutDto> MarkPaidAsync(
        int adminUserId, int creatorUserId, MarkCreatorPayoutPaidRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>Admin huỷ một lần chi kèm lý do: số dư được cộng lại, thông báo Creator, audit log.</summary>
    Task<CreatorPayoutDto> CancelPayoutAsync(
        int adminUserId, int payoutId, CancelCreatorPayoutRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Worker: từ ngày <see cref="PeriodCloseDay"/> (giờ Việt Nam), chốt kỳ của tháng trước nếu chưa chốt và cộng thu nhập
    /// của kỳ vào số dư từng Creator. Trả true khi vừa chốt.
    /// </summary>
    Task<bool> CloseDuePeriodAsync(DateTime now, CancellationToken cancellationToken = default);
}
