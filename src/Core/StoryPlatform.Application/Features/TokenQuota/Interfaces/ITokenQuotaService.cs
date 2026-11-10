using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.TokenQuota.DTOs;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.TokenQuota.Interfaces;

public interface ITokenQuotaService
{
    Task<TokenQuotaStatusDto> GetStatusForChildAsync(
        int requestingUserId, int childProfileId, CancellationToken cancellationToken = default);

    Task<MyQuotaDto> GetMyQuotaAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>Gói và lượt của một tài khoản, cho Admin xem; không cấp lượt dùng thử.</summary>
    Task<MyQuotaDto> GetQuotaForUserAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>Admin cấp hoặc trừ lượt cho một tài khoản phụ huynh, có lý do (UC-L2-08).</summary>
    Task<CreditAdjustmentDto> AdjustAsync(
        int adminUserId, AdjustCreditsRequestDto request, CancellationToken cancellationToken = default);

    Task<PagedResult<CreditAdjustmentDto>> ListAdjustmentsAsync(
        int? userId, int pageIndex, int pageSize, CancellationToken cancellationToken = default);

    Task EnsureWithinQuotaAsync(int childProfileId, CancellationToken cancellationToken = default);

    Task IncrementUsageAsync(int childProfileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cấp một lô lượt AI. Trả false và không làm gì nếu amount không dương hoặc đã có lô gắn paymentId này.
    /// </summary>
    Task<bool> GrantAsync(
        int userId, CreditSource source, int amount, DateTime? expiresAt, int? paymentId,
        CancellationToken cancellationToken = default);
}
