using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.TokenQuota.DTOs;
using StoryPlatform.Domain.Entities;
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

    /// <summary>
    /// Giữ 1 lượt AI của phụ huynh cho <paramref name="story"/> (409 nếu hết): lượt rời lô ngay, được trừ chính thức
    /// khi AI sinh xong nội dung và được trả khi lỗi, bị chặn hoặc từ chối dàn ý. Chạy trong transaction của nơi gọi.
    /// </summary>
    Task IncrementUsageAsync(int childProfileId, Story? story, CancellationToken cancellationToken = default);

    /// <summary>
    /// Thử lại sau khi job AI lỗi đã trả lượt: giữ lại 1 lượt cho truyện (409 nếu hết). Không làm gì và trả false
    /// nếu truyện đang giữ lượt hoặc đã được trừ lượt.
    /// </summary>
    Task<bool> HoldStoryCreditForRetryAsync(int storyId, CancellationToken cancellationToken = default);

    /// <summary>Trả lượt đang giữ của truyện; lượt đã trừ chính thức không bị trả. Gọi lại không trả hai lần.</summary>
    Task ReleaseStoryCreditAsync(int storyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cấp một lô lượt AI. Trả false và không làm gì nếu amount không dương hoặc đã có lô gắn paymentId này.
    /// </summary>
    Task<bool> GrantAsync(
        int userId, CreditSource source, int amount, DateTime? expiresAt, int? paymentId,
        CreditType creditType = CreditType.AI, CancellationToken cancellationToken = default);

    /// <summary>Creator gửi yêu cầu tạo truyện AI: 409 nếu hết lượt (hạn mức tháng và lượt Gói Creator).</summary>
    Task EnsureCreatorWithinQuotaAsync(int creatorUserId, CancellationToken cancellationToken = default);

    /// <summary>Giữ 1 lượt AI của Creator cho <paramref name="story"/>, hạn mức tháng trước; như <see cref="IncrementUsageAsync"/>.</summary>
    Task IncrementCreatorUsageAsync(int creatorUserId, Story? story, CancellationToken cancellationToken = default);

    /// <summary>
    /// Trừ 1 lượt gửi duyệt cho mục kiểm duyệt vừa tạo (409 nếu hết). Gửi lại sau khi bị yêu cầu sửa
    /// (<paramref name="changesRequestedItemId"/>) thì không trừ thêm. Chạy trong transaction của nơi gọi.
    /// </summary>
    Task ChargeSubmissionAsync(
        int creatorUserId, int moderationItemId, int? changesRequestedItemId, CancellationToken cancellationToken = default);

    /// <summary>Hoàn lượt gửi duyệt của mục bị từ chối hoặc rút khi còn chờ; gọi lại không hoàn hai lần.</summary>
    Task RefundSubmissionAsync(int moderationItemId, CancellationToken cancellationToken = default);

    Task<CreatorQuotaDto> GetCreatorQuotaAsync(int creatorUserId, CancellationToken cancellationToken = default);
}
