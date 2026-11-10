using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.Payments.Interfaces;

/// <summary>
/// Chu kỳ gói tháng của phụ huynh (Luồng 2, 2g): áp gói khi giao dịch Paid, chuyển chu kỳ, nhắc gia hạn.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>Gói còn hiệu lực = dòng Active có EndAt sau <paramref name="now"/>; null khi ở mức Miễn phí.</summary>
    Task<UserSubscription?> GetActiveAsync(int userId, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Áp gói của một giao dịch đã Paid theo loại gói. Idempotent theo Id giao dịch: gọi lại không tạo thêm
    /// chu kỳ hay lô lượt. Lỗi được ném ra sau khi rollback để nơi gọi ghi audit.
    /// </summary>
    Task ApplyPaidTransactionAsync(
        PaymentTransaction transaction, SubscriptionPlan plan, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Đưa chu kỳ của một tài khoản về đúng thời điểm <paramref name="now"/>: hết hạn dòng Active đã qua EndAt,
    /// kích hoạt dòng Scheduled đến hạn. Không tự mở transaction.
    /// </summary>
    Task AdvanceCyclesAsync(int userId, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>Chuyển chu kỳ cho mọi tài khoản đến hạn rồi gửi nhắc gia hạn. Trả số tài khoản đã chuyển chu kỳ.</summary>
    Task<int> SweepAsync(DateTime now, CancellationToken cancellationToken = default);
}
