using StoryPlatform.Application.Features.CreatorTiers.DTOs;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.CreatorTiers.Interfaces;

/// <summary>
/// Xét cấp Creator đối tác (Luồng 5, 5g): mỗi tháng (giờ Việt Nam) một lần cho mỗi Creator Active, trước khi cấp hạn mức tháng.
/// Đạt cả 4 tiêu chí → đối tác; đối tác không đạt 2 tháng liên tiếp → Creator thường. Truyện bị gỡ vì vi phạm hoặc Admin thu
/// hồi → mất cấp ngay.
/// </summary>
public interface ICreatorTierService
{
    /// <summary>
    /// Bảo đảm Creator đã được xét cho tháng của <paramref name="now"/> rồi trả cấp hiện tại. Ghi vào ngữ cảnh (và transaction,
    /// nếu có) của nơi gọi, không tự mở transaction — dùng được lúc cấp hạn mức tháng bên trong việc trừ lượt.
    /// </summary>
    Task<CreatorTier> EnsureEvaluatedAsync(int creatorUserId, DateTime now, CancellationToken cancellationToken = default);

    /// <summary>Worker: xét mọi Creator Active chưa được xét trong tháng. Trả số Creator đã xét.</summary>
    Task<int> EvaluateDueAsync(DateTime now, CancellationToken cancellationToken = default);

    Task<CreatorTierOverviewDto> GetOverviewAsync(int creatorUserId, CancellationToken cancellationToken = default);

    /// <summary>Admin thu hồi cấp đối tác (lý do bắt buộc). Lần xét tháng sau vẫn xét bình thường.</summary>
    Task<CreatorTierOverviewDto> RevokeAsync(
        int adminUserId, int creatorUserId, RevokePartnerTierRequestDto request, CancellationToken cancellationToken = default);
}
