using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.Payments.DTOs;

namespace StoryPlatform.Application.Features.Payments.Interfaces;

/// <summary>
/// Đối soát giao dịch đã Paid nhưng chưa được áp gói (UC-L2-13).
/// </summary>
public interface IPaymentReconciliationService
{
    /// <summary>
    /// Tìm giao dịch Paid chưa được áp gói và tạo ngoại lệ Open cho từng giao dịch. Không tự áp gói.
    /// Trả số ngoại lệ mới.
    /// </summary>
    Task<int> DetectAsync(DateTime now, CancellationToken cancellationToken = default);

    /// <summary>Danh sách ngoại lệ, mới nhất trước. <paramref name="status"/> bỏ trống thì lấy Open.</summary>
    Task<PagedResult<ReconciliationExceptionDto>> ListAsync(
        string? status, int pageIndex, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Áp lại gói bằng đúng bước áp gói của webhook (không cộng hai lần) rồi đóng ngoại lệ.</summary>
    Task<ReconciliationExceptionDto> ResolveAsync(
        int adminUserId, int exceptionId, CancellationToken cancellationToken = default);
}
