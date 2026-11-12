using StoryPlatform.Application.Features.PlanBenefits.Interfaces;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.BusinessReports.DTOs;
using StoryPlatform.Application.Features.BusinessReports.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.BusinessReports.Services;

public class RevenueStatisticsService : IRevenueStatisticsService
{
    // Tỷ lệ quỹ Creator theo bản chốt gói dịch vụ; Admin cấu hình được ở F-17 (chưa làm).

    // Luôn hiện đủ các loại gói đang bán, kể cả khi kỳ không có giao dịch.
    private static readonly PlanType[] ReportedPlanTypes =
        [PlanType.Reading, PlanType.Creative, PlanType.AddOn, PlanType.CreatorPack];

    private readonly IUnitOfWork _unitOfWork;
    private readonly IPlanSettingsProvider? _planSettings;

    /// <param name="planSettings">Tỷ lệ quỹ Creator do Admin cấu hình; null thì dùng mặc định 30% / 10%.</param>
    public RevenueStatisticsService(IUnitOfWork unitOfWork, IPlanSettingsProvider? planSettings = null)
    {
        _unitOfWork = unitOfWork;
        _planSettings = planSettings;
    }

    public async Task<RevenueStatisticsDto> GetAsync(
        DateTime periodStart, DateTime periodEnd, CancellationToken cancellationToken = default)
    {
        var (from, toExclusive) = ReportPeriod.Resolve(periodStart, periodEnd);
        var settings = _planSettings == null ? new PlanSettings() : await _planSettings.GetAsync(cancellationToken);

        var transactions = _unitOfWork.Repository<PaymentTransaction>();
        // Doanh thu: chỉ giao dịch Paid, theo PaidAt, dùng số tiền lưu trên giao dịch.
        var paid = await transactions.FindAsync(
            item => item.Status == PaymentStatus.Paid && item.PaidAt >= from && item.PaidAt < toExclusive,
            cancellationToken: cancellationToken);
        var unpaidCreated = await transactions.FindAsync(
            item => item.Status != PaymentStatus.Paid && item.CreatedAt >= from && item.CreatedAt < toExclusive,
            cancellationToken: cancellationToken);
        var plans = (await _unitOfWork.Repository<SubscriptionPlan>().GetAllAsync(cancellationToken))
            .ToDictionary(plan => plan.Id);

        var byPlan = paid
            .GroupBy(item => item.PlanId)
            .Select(group =>
            {
                plans.TryGetValue(group.Key, out var plan);
                return new PlanRevenueDto
                {
                    PlanId = group.Key,
                    PlanName = plan?.Name ?? string.Empty,
                    PlanType = (plan?.PlanType ?? PlanType.Legacy).ToString(),
                    TransactionCount = group.Count(),
                    RevenueVnd = group.Sum(item => (long)item.Amount)
                };
            })
            .OrderBy(row => row.PlanId)
            .ToList();

        var byPlanType = ReportedPlanTypes
            .Select(type => type.ToString())
            .Union(byPlan.Select(row => row.PlanType))
            .Select(type => new PlanTypeRevenueDto
            {
                PlanType = type,
                TransactionCount = byPlan.Where(row => row.PlanType == type).Sum(row => row.TransactionCount),
                RevenueVnd = byPlan.Where(row => row.PlanType == type).Sum(row => row.RevenueVnd)
            })
            .ToList();
        long RevenueOf(PlanType type) => byPlanType.Single(row => row.PlanType == type.ToString()).RevenueVnd;

        // Người mua là phụ huynh: giao dịch của gói dành cho phụ huynh (Gói Creator do Creator mua nên không tính).
        // Mua lại: người mua trong kỳ mà tính đến cuối kỳ đã có từ 2 giao dịch Paid như vậy.
        bool BoughtByParent(PaymentTransaction item) =>
            !plans.TryGetValue(item.PlanId, out var plan) || plan.Audience == PlanAudience.User;
        var buyers = paid.Where(BoughtByParent).Select(item => item.PayerUserId).ToHashSet();
        var paidUntilPeriodEnd = await transactions.FindAsync(
            item => item.Status == PaymentStatus.Paid && item.PaidAt < toExclusive,
            cancellationToken: cancellationToken);
        var repeatBuyers = paidUntilPeriodEnd
            .Where(item => buyers.Contains(item.PayerUserId) && BoughtByParent(item))
            .GroupBy(item => item.PayerUserId)
            .Count(group => group.Count() >= 2);

        var manualMarks = (await _unitOfWork.Repository<AuditLog>().FindAsync(
                log => log.Action == "PaymentMarkedPaidManually" && log.EntityType == nameof(PaymentTransaction),
                cancellationToken: cancellationToken))
            .Select(log => log.EntityId)
            .ToHashSet();
        var manuallyPaid = paid.Where(item => manualMarks.Contains(item.Id)).ToList();

        // Lượt cấp và lượt dùng chỉ tính lượt AI; lượt gửi duyệt của Creator là đơn vị khác.
        var lotsGranted = await _unitOfWork.Repository<CreditLot>().FindAsync(
            lot => lot.CreditType == CreditType.AI && lot.CreatedAt >= from && lot.CreatedAt < toExclusive,
            cancellationToken: cancellationToken);
        var usages = await _unitOfWork.Repository<CreditUsage>().FindAsync(
            usage => usage.Reason != CreditUsageReason.PublicSubmission
                     && usage.CreatedAt >= from && usage.CreatedAt < toExclusive,
            cancellationToken: cancellationToken);

        return new RevenueStatisticsDto
        {
            PeriodStart = from,
            PeriodEnd = toExclusive.AddDays(-1),
            TotalRevenueVnd = paid.Sum(item => (long)item.Amount),
            PaidTransactionCount = paid.Count,
            TransactionsByStatus = new TransactionStatusCountsDto
            {
                Paid = paid.Count,
                Pending = unpaidCreated.Count(item => item.Status == PaymentStatus.Pending),
                Expired = unpaidCreated.Count(item => item.Status == PaymentStatus.Expired),
                MismatchAmount = unpaidCreated.Count(item => item.Status == PaymentStatus.MismatchAmount)
            },
            RevenueByPlan = byPlan,
            RevenueByPlanType = byPlanType,
            BuyingParents = buyers.Count,
            RepeatBuyingParents = repeatBuyers,
            ManuallyMarkedPaidCount = manuallyPaid.Count,
            ManuallyMarkedPaidRevenueVnd = manuallyPaid.Sum(item => (long)item.Amount),
            CreditsGranted = lotsGranted.Sum(lot => lot.Total),
            CreditsGrantedBySource = lotsGranted
                .GroupBy(lot => lot.Source)
                .OrderBy(group => group.Key)
                .Select(group => new CreditSourceCountDto { Source = group.Key.ToString(), Credits = group.Sum(lot => lot.Total) })
                .ToList(),
            CreditsUsed = usages.Sum(usage => usage.Amount),
            ReadingFundRate = settings.ReadingFundPercent / 100m,
            CreativeFundRate = settings.CreativeFundPercent / 100m,
            // Add-on và gói khác không vào quỹ. Làm tròn xuống đến đồng.
            CreatorFundVnd =
                (RevenueOf(PlanType.Reading) * settings.ReadingFundPercent + RevenueOf(PlanType.Creative) * settings.CreativeFundPercent) / 100
        };
    }
}

/// <summary>
/// Kỳ báo cáo tính trọn ngày theo UTC và gồm cả ngày kết thúc: [PeriodStart 00:00, ngày sau PeriodEnd 00:00).
/// Màn Admin gửi ngày đầu và ngày cuối kỳ, nên ngày cuối phải được tính đủ.
/// </summary>
public static class ReportPeriod
{
    public static (DateTime From, DateTime ToExclusive) Resolve(DateTime periodStart, DateTime periodEnd)
    {
        var from = DateTime.SpecifyKind(periodStart.Date, DateTimeKind.Utc);
        var lastDay = DateTime.SpecifyKind(periodEnd.Date, DateTimeKind.Utc);
        if (lastDay < from)
        {
            throw new BadRequestException("PeriodEnd không được trước PeriodStart.");
        }

        return (from, lastDay.AddDays(1));
    }
}
