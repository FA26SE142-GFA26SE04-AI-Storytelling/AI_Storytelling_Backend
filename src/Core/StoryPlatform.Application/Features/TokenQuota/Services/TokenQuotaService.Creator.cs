using StoryPlatform.Application.Common.Time;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.TokenQuota.DTOs;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.TokenQuota.Services;

/// <summary>
/// Lượt của Content Creator (Luồng 2, 2f): hạn mức tháng miễn phí (2 lượt AI, 5 lượt gửi duyệt) và lượt mua từ
/// Gói Creator. Hạn mức tháng hết hạn cuối tháng nên luôn được dùng trước lượt đã mua (lô sắp hết hạn dùng trước).
/// </summary>
public partial class TokenQuotaService
{
    // Hạn mức tháng theo bản chốt; Admin cấu hình được ở F-17 (chưa làm). Creator đối tác (6 và 10) để sau Defense 1.1.

    private const string CreatorOutOfAiCreditsMessage =
        "Bạn đã hết lượt tạo truyện AI. Hãy chờ hạn mức tháng sau hoặc mua Gói nhà sáng tạo để tạo thêm.";
    private const string CreatorOutOfSubmitCreditsMessage =
        "Bạn đã hết lượt gửi duyệt. Hãy chờ hạn mức tháng sau hoặc mua Gói nhà sáng tạo để gửi thêm truyện.";

    public async Task EnsureCreatorWithinQuotaAsync(int creatorUserId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        await EnsureCreatorMonthlyGrantedAsync(creatorUserId, now, cancellationToken);
        var lots = await GetUsableLotsAsync(creatorUserId, now, cancellationToken);
        if (lots.Sum(lot => lot.Remaining) <= 0)
        {
            throw new ConflictException(CreatorOutOfAiCreditsMessage);
        }
    }

    public async Task IncrementCreatorUsageAsync(int creatorUserId, Story? story, CancellationToken cancellationToken = default)
    {
        // Khoá theo tài khoản trước khi đọc lô, như IncrementUsageAsync của phụ huynh.
        await _unitOfWork.AcquireTransactionLockAsync(creatorUserId, cancellationToken);
        await EnsureCreatorMonthlyGrantedAsync(creatorUserId, DateTime.UtcNow, cancellationToken);
        await HoldOneCreditAsync(creatorUserId, story, CreatorOutOfAiCreditsMessage, cancellationToken);
    }

    public async Task ChargeSubmissionAsync(
        int creatorUserId, int moderationItemId, int? changesRequestedItemId,
        CancellationToken cancellationToken = default, int? eventId = null)
    {
        await _unitOfWork.AcquireTransactionLockAsync(creatorUserId, cancellationToken);
        var now = DateTime.UtcNow;
        var usages = _unitOfWork.Repository<CreditUsage>();

        // Gửi lại sau khi bị yêu cầu sửa: không trừ thêm, lượt của lần gửi trước đi theo lần gửi lại.
        if (changesRequestedItemId.HasValue)
        {
            var carried = await FindActiveSubmissionUsageAsync(changesRequestedItemId.Value, cancellationToken);
            if (carried != null)
            {
                carried.ModerationItemId = moderationItemId;
                carried.UpdatedAt = now;
                usages.Update(carried);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return;
            }

            // Lượt đã đi theo một lần gửi lại rồi được hoàn (Creator rút yêu cầu): lần gửi này phải trả lượt mới.
            // Không có dấu vết lượt nào (yêu cầu gửi trước khi có lượt gửi duyệt): vẫn miễn phí.
            if (!await WasCarriedAndRefundedAsync(changesRequestedItemId.Value, cancellationToken))
            {
                return;
            }
        }

        await EnsureCreatorMonthlyGrantedAsync(creatorUserId, now, cancellationToken);
        var lots = await GetUsableLotsAsync(creatorUserId, now, cancellationToken, CreditType.Submit,
            includeEventGrants: eventId.HasValue);
        if (eventId.HasValue && lots.Any(lot => lot.Source == CreditSource.EventGrant))
        {
            lots = await GetStoryLotsAsync(creatorUserId, null, CreditType.Submit, eventId, cancellationToken, lots);
        }
        var lot = OrderForUse(lots, preferEventGrants: eventId.HasValue).FirstOrDefault()
                  ?? throw new ConflictException(CreatorOutOfSubmitCreditsMessage);
        lot.Remaining -= 1;
        lot.UpdatedAt = now;
        _unitOfWork.Repository<CreditLot>().Update(lot);
        await usages.AddAsync(
            new CreditUsage
            {
                UserId = creatorUserId,
                LotId = lot.Id,
                Amount = 1,
                Reason = CreditUsageReason.PublicSubmission,
                ModerationItemId = moderationItemId
            },
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task RefundSubmissionAsync(int moderationItemId, CancellationToken cancellationToken = default)
    {
        var usage = await FindActiveSubmissionUsageAsync(moderationItemId, cancellationToken);
        if (usage == null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var lot = await _unitOfWork.Repository<CreditLot>().GetByIdAsync(usage.LotId, cancellationToken);
        if (lot != null)
        {
            // Hoàn về đúng lô đã trừ; lô hạn mức tháng đã qua tháng thì lượt hoàn cũng hết hạn theo.
            lot.Remaining += usage.Amount;
            lot.UpdatedAt = now;
            _unitOfWork.Repository<CreditLot>().Update(lot);
        }

        usage.RefundedAt = now;
        usage.UpdatedAt = now;
        _unitOfWork.Repository<CreditUsage>().Update(usage);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<CreatorQuotaDto> GetCreatorQuotaAsync(int creatorUserId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var tier = await EnsureCreatorMonthlyGrantedAsync(creatorUserId, now, cancellationToken);
        var ai = await GetUsableLotsAsync(creatorUserId, now, cancellationToken);
        var submit = await GetUsableLotsAsync(creatorUserId, now, cancellationToken, CreditType.Submit);
        var settings = await SettingsAsync(cancellationToken);
        var partner = tier == CreatorTier.Partner;

        return new CreatorQuotaDto
        {
            AiCredits = Balance(ai, partner ? settings.PartnerMonthlyAiCredits : settings.CreatorMonthlyAiCredits),
            SubmitCredits = Balance(submit, partner ? settings.PartnerMonthlySubmitCredits : settings.CreatorMonthlySubmitCredits),
            MonthlyAllowanceResetsAt = CurrentCreatorMonthEnd(now)
        };
    }

    /// <summary>
    /// Cấp hạn mức tháng cho Creator đang Active ở lần đầu cần dùng trong tháng (thay cho worker chạy ngày 1;
    /// kết quả với Creator như nhau). Không cộng dồn: lô tháng trước đã hết hạn cuối tháng. Trước khi cấp, Creator được
    /// xét cấp đối tác cho tháng này (Luồng 5, 5g) để Creator vừa lên đối tác nhận ngay hạn mức 6 + 10. Trả cấp hiện tại.
    /// </summary>
    private async Task<CreatorTier> EnsureCreatorMonthlyGrantedAsync(int creatorUserId, DateTime now, CancellationToken cancellationToken)
    {
        var user = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(creatorUserId, cancellationToken);
        if (user is not { Role: UserRole.ContentCreator, Status: AccountStatus.Active } || user.IsDeleted)
        {
            return user?.CreatorTier ?? CreatorTier.Standard;
        }

        var monthEnd = CurrentCreatorMonthEnd(now);
        var repo = _unitOfWork.Repository<CreditLot>();
        var missing = new List<CreditType>();
        foreach (var type in new[] { CreditType.AI, CreditType.Submit })
        {
            if (!await repo.ExistsAsync(
                    lot => lot.UserId == creatorUserId && lot.Source == CreditSource.CreatorMonthly
                           && lot.CreditType == type && lot.ExpiresAt == monthEnd,
                    cancellationToken))
            {
                missing.Add(type);
            }
        }

        if (missing.Count == 0)
        {
            return user.CreatorTier;
        }

        // Xét cấp chạy trước khi cấp hạn mức tháng (UC-L2-11); kết quả đọc từ scope riêng nên không dùng user đang track.
        var tier = _creatorTiers == null
            ? user.CreatorTier
            : await _creatorTiers.EnsureEvaluatedAsync(creatorUserId, now, cancellationToken);
        var partner = tier == CreatorTier.Partner;

        // Lô đã cấp trong tháng giữ số cũ; cấu hình mới áp dụng từ lần cấp sau.
        var settings = await SettingsAsync(cancellationToken);
        foreach (var type in missing)
        {
            var amount = type == CreditType.AI
                ? partner ? settings.PartnerMonthlyAiCredits : settings.CreatorMonthlyAiCredits
                : partner ? settings.PartnerMonthlySubmitCredits : settings.CreatorMonthlySubmitCredits;
            await repo.AddAsync(
                new CreditLot
                {
                    UserId = creatorUserId, CreditType = type, Source = CreditSource.CreatorMonthly,
                    Total = amount, Remaining = amount, ExpiresAt = monthEnd
                },
                cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return tier;
    }

    private async Task<CreditUsage?> FindActiveSubmissionUsageAsync(int moderationItemId, CancellationToken cancellationToken) =>
        await _unitOfWork.Repository<CreditUsage>().FirstOrDefaultAsync(
            usage => usage.ModerationItemId == moderationItemId
                     && usage.Reason == CreditUsageReason.PublicSubmission
                     && usage.RefundedAt == null,
            cancellationToken: cancellationToken);

    /// <summary>Lượt của lần bị yêu cầu sửa đã đi theo một lần gửi lại và lần đó đã được hoàn lượt.</summary>
    private async Task<bool> WasCarriedAndRefundedAsync(int changesRequestedItemId, CancellationToken cancellationToken)
    {
        var resubmissions = await _unitOfWork.Repository<ModerationItem>().FindAsync(
            item => item.PreviousItemId == changesRequestedItemId, cancellationToken: cancellationToken);
        var resubmissionIds = resubmissions.Select(item => item.Id).ToList();
        if (resubmissionIds.Count == 0)
        {
            return false;
        }

        return await _unitOfWork.Repository<CreditUsage>().ExistsAsync(
            usage => usage.ModerationItemId != null && resubmissionIds.Contains(usage.ModerationItemId.Value)
                     && usage.Reason == CreditUsageReason.PublicSubmission
                     && usage.RefundedAt != null,
            cancellationToken);
    }

    /// <summary>00:00 ngày 1 tháng sau theo giờ Việt Nam, tính ra UTC.</summary>
    private static DateTime CurrentCreatorMonthEnd(DateTime nowUtc)
    {
        return VietnamTime.StartOfNextMonthUtc(nowUtc);
    }

    private static CreatorCreditBalanceDto Balance(IReadOnlyList<CreditLot> lots, int monthlyAllowance)
    {
        int Remaining(CreditSource source) => lots.Where(lot => lot.Source == source).Sum(lot => lot.Remaining);
        var monthly = Remaining(CreditSource.CreatorMonthly);
        var purchased = Remaining(CreditSource.CreatorPack);
        var total = lots.Sum(lot => lot.Remaining);
        return new CreatorCreditBalanceDto
        {
            MonthlyAllowance = monthlyAllowance,
            MonthlyRemaining = monthly,
            PurchasedRemaining = purchased,
            OtherRemaining = total - monthly - purchased,
            TotalRemaining = total
        };
    }
}
