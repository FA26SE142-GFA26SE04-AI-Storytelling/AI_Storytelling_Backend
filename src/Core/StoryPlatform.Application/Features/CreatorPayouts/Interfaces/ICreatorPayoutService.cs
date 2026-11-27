using StoryPlatform.Application.Features.CreatorPayouts.DTOs;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.CreatorPayouts.Interfaces;

/// <summary>
/// Chi trả cho Creator (Luồng 5, 5h). Hệ thống không chuyển tiền: Admin chuyển khoản ngoài hệ thống rồi đánh dấu Đã chi;
/// hệ thống lưu thông tin nhận tiền, số dư (sổ bút toán) và lịch sử các lần chi. Số tiền là số trước thuế.
/// </summary>
public interface ICreatorPayoutService
{
    /// <summary>Creator xem số dư, thông tin nhận tiền và lịch sử của chính mình.</summary>
    Task<CreatorPayoutOverviewDto> GetOverviewAsync(int creatorUserId, CancellationToken cancellationToken = default);

    /// <summary>Creator nhập hoặc sửa thông tin nhận tiền; mỗi lần sửa ghi audit log.</summary>
    Task<CreatorPayoutAccountDto> UpdatePayoutAccountAsync(
        int creatorUserId, UpdateCreatorPayoutAccountRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>Danh sách chi: Creator Active, có số dư lớn hơn 0, đạt mức chi tối thiểu Admin đặt
    /// (<see cref="PlanSettings.MinimumPayoutVnd"/>) và đã có thông tin nhận tiền.</summary>
    Task<IReadOnlyList<CreatorPayoutDueDto>> GetDueAsync(CancellationToken cancellationToken = default);

    /// <summary>Admin đánh dấu Đã chi đúng bằng số dư: ghi lần chi, số dư về 0, thông báo Creator, audit log.</summary>
    Task<CreatorPayoutDto> MarkPaidAsync(
        int adminUserId, int creatorUserId, MarkCreatorPayoutPaidRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>Admin huỷ một lần chi kèm lý do: số dư được cộng lại, thông báo Creator, audit log.</summary>
    Task<CreatorPayoutDto> CancelPayoutAsync(
        int adminUserId, int payoutId, CancelCreatorPayoutRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Worker: từ ngày chốt Admin đặt (<see cref="PlanSettings.CreatorPeriodCloseDay"/>, giờ Việt Nam), chốt phần chưa chốt của tháng trước (cả tháng, hoặc
    /// phần còn lại sau lần Chốt kỳ ngay) và cộng thu nhập của kỳ vào số dư từng Creator. Trả true khi vừa chốt.
    /// </summary>
    Task<bool> CloseDuePeriodAsync(DateTime now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Admin bấm Chốt kỳ ngay (dùng khi demo): chốt từ ngày sau kỳ đã chốt gần nhất (hoặc ngày 1 tháng trước nếu tháng trước
    /// chưa chốt) đến hết hôm nay (giờ Việt Nam) và cộng thu nhập vào số dư ngay. Lượt đọc còn lại của hôm nay không được tính;
    /// lần chốt sau bắt đầu từ ngày mai. Ghi audit log.
    /// </summary>
    Task<CreatorEarningPeriodDto> CloseNowAsync(int adminUserId, DateTime now, CancellationToken cancellationToken = default);
}
