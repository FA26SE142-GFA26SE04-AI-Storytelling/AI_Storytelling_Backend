using StoryPlatform.Application.Features.PlanBenefits.Services;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.BusinessReports.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.UnitTests.Features.TokenQuota;
using Xunit;

namespace StoryPlatform.UnitTests.Features.BusinessReports;

public class RevenueStatisticsServiceTests
{
    private const int ReadingPlanId = 2;
    private const int CreativePlanId = 3;
    private const int AddOnPlanId = 4;

    // Kỳ báo cáo tính trọn ngày, gồm cả ngày kết thúc: 01/11 00:00 → hết ngày 30/11 (UTC).
    private static readonly DateTime PeriodStart = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PeriodEnd = new(2026, 11, 30, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime MidPeriod = new(2026, 11, 15, 9, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryUnitOfWork _store = new();
    private readonly RevenueStatisticsService _sut;
    private int _nextId = 1;

    public RevenueStatisticsServiceTests()
    {
        _store.Seed(new SubscriptionPlan { Id = 1, Name = "Personal", PlanType = PlanType.Legacy, PriceVnd = 49000, AiCredits = 50, IsActive = false });
        _store.Seed(new SubscriptionPlan { Id = ReadingPlanId, Name = "Gói Đọc", PlanType = PlanType.Reading, PriceVnd = 49000, AiCredits = 0 });
        _store.Seed(new SubscriptionPlan { Id = CreativePlanId, Name = "Gói Sáng tạo", PlanType = PlanType.Creative, PriceVnd = 99000, AiCredits = 4 });
        _store.Seed(new SubscriptionPlan { Id = AddOnPlanId, Name = "Add-on", PlanType = PlanType.AddOn, PriceVnd = 29000, AiCredits = 2 });
        _sut = new RevenueStatisticsService(_store);
    }

    private PaymentTransaction Paid(int planId, int payerUserId, DateTime paidAt, int? amount = null) =>
        Transaction(planId, payerUserId, PaymentStatus.Paid, createdAt: paidAt.AddMinutes(-3), paidAt, amount);

    private PaymentTransaction Transaction(
        int planId, int payerUserId, PaymentStatus status, DateTime createdAt, DateTime? paidAt = null, int? amount = null)
    {
        var id = _nextId++;
        return _store.Seed(new PaymentTransaction
        {
            Id = id,
            PlanId = planId,
            PayerUserId = payerUserId,
            TransactionCode = $"SEPAYREV{id:D7}",
            Amount = amount ?? _store.Items<SubscriptionPlan>().Single(plan => plan.Id == planId).PriceVnd,
            Status = status,
            CreatedAt = createdAt,
            PaidAt = paidAt,
            ExpiresAt = createdAt.AddMinutes(15)
        });
    }

    private void MarkedPaidByAdmin(PaymentTransaction transaction) => _store.Seed(new AuditLog
    {
        ActorUserId = 9, Action = "PaymentMarkedPaidManually", EntityType = nameof(PaymentTransaction),
        EntityId = transaction.Id, OccurredAt = transaction.PaidAt ?? MidPeriod
    });

    private void Lot(CreditSource source, int total, DateTime createdAt) => _store.Seed(new CreditLot
    {
        UserId = 1, Source = source, Total = total, Remaining = total, CreatedAt = createdAt
    });

    private void Usage(int amount, CreditUsageReason reason, DateTime createdAt) => _store.Seed(new CreditUsage
    {
        UserId = 1, LotId = 1, Amount = amount, Reason = reason, CreatedAt = createdAt
    });

    [Fact]
    public async Task GetAsync_CountsOnlyPaidTransactionsByPaidAtUsingStoredAmount()
    {
        Paid(ReadingPlanId, 1, MidPeriod);
        Paid(CreativePlanId, 1, MidPeriod.AddDays(2));
        Paid(CreativePlanId, 2, PeriodStart);                              // đúng 00:00 ngày đầu kỳ
        Paid(CreativePlanId, 4, PeriodEnd.AddHours(23).AddMinutes(30));    // cuối ngày cuối kỳ: vẫn thuộc kỳ
        Paid(AddOnPlanId, 3, MidPeriod);
        Paid(CreativePlanId, 5, PeriodStart.AddSeconds(-1));               // ngay trước kỳ
        Paid(CreativePlanId, 5, PeriodEnd.AddDays(1));                     // 00:00 ngày sau kỳ
        Transaction(CreativePlanId, 6, PaymentStatus.MismatchAmount, MidPeriod);
        Transaction(CreativePlanId, 6, PaymentStatus.Pending, MidPeriod);
        Transaction(CreativePlanId, 6, PaymentStatus.Expired, MidPeriod);
        // Tạo trong kỳ nhưng trả sau kỳ: không tính vào doanh thu của kỳ này.
        Transaction(ReadingPlanId, 7, PaymentStatus.Paid, createdAt: PeriodEnd, paidAt: PeriodEnd.AddDays(3));

        var result = await _sut.GetAsync(PeriodStart, PeriodEnd);

        Assert.Equal((375000L, 5), (result.TotalRevenueVnd, result.PaidTransactionCount));
        Assert.Equal((PeriodStart, PeriodEnd), (result.PeriodStart, result.PeriodEnd));
    }

    [Fact]
    public async Task GetAsync_UsesAmountStoredOnTransaction_NotTheCurrentPlanPrice()
    {
        Paid(CreativePlanId, 1, MidPeriod);
        _store.Items<SubscriptionPlan>().Single(plan => plan.Id == CreativePlanId).PriceVnd = 120000;

        var result = await _sut.GetAsync(PeriodStart, PeriodEnd);

        Assert.Equal(99000L, result.TotalRevenueVnd);
    }

    [Fact]
    public async Task GetAsync_CountsTransactionsByStatus()
    {
        Paid(ReadingPlanId, 1, MidPeriod);
        Paid(CreativePlanId, 2, MidPeriod);
        Transaction(CreativePlanId, 3, PaymentStatus.Pending, MidPeriod);
        Transaction(CreativePlanId, 3, PaymentStatus.Expired, MidPeriod);
        Transaction(AddOnPlanId, 3, PaymentStatus.Expired, PeriodEnd.AddHours(20));
        Transaction(CreativePlanId, 4, PaymentStatus.MismatchAmount, MidPeriod);
        Transaction(CreativePlanId, 5, PaymentStatus.Pending, PeriodStart.AddDays(-2));   // tạo trước kỳ
        Transaction(CreativePlanId, 5, PaymentStatus.Expired, PeriodEnd.AddDays(1));      // tạo sau kỳ

        var result = await _sut.GetAsync(PeriodStart, PeriodEnd);

        Assert.Equal(
            (2, 1, 2, 1),
            (result.TransactionsByStatus.Paid, result.TransactionsByStatus.Pending,
                result.TransactionsByStatus.Expired, result.TransactionsByStatus.MismatchAmount));
    }

    [Fact]
    public async Task GetAsync_BreaksRevenueDownByPlanAndPlanType()
    {
        Paid(ReadingPlanId, 1, MidPeriod);
        Paid(CreativePlanId, 2, MidPeriod);
        Paid(CreativePlanId, 3, MidPeriod, amount: 120000);   // mua sau khi gói đổi giá
        Paid(AddOnPlanId, 2, MidPeriod);
        _store.Seed(new SubscriptionPlan { Id = 5, Name = "Gói Sáng tạo Tết", PlanType = PlanType.Creative, PriceVnd = 79000, AiCredits = 4 });
        Paid(5, 4, MidPeriod);

        var result = await _sut.GetAsync(PeriodStart, PeriodEnd);

        Assert.Equal(
            ["2:Gói Đọc:Reading:1:49000", "3:Gói Sáng tạo:Creative:2:219000", "4:Add-on:AddOn:1:29000", "5:Gói Sáng tạo Tết:Creative:1:79000"],
            result.RevenueByPlan.Select(row => $"{row.PlanId}:{row.PlanName}:{row.PlanType}:{row.TransactionCount}:{row.RevenueVnd}"));
        Assert.Equal(
            ["Reading:1:49000", "Creative:3:298000", "AddOn:1:29000", "CreatorPack:0:0"],
            result.RevenueByPlanType.Select(row => $"{row.PlanType}:{row.TransactionCount}:{row.RevenueVnd}"));
    }

    [Fact]
    public async Task GetAsync_LegacyPlanRevenue_IsListedUnderItsOwnType()
    {
        Paid(1, 1, MidPeriod);

        var result = await _sut.GetAsync(PeriodStart, PeriodEnd);

        Assert.Contains(result.RevenueByPlanType, row => row.PlanType == "Legacy" && row.RevenueVnd == 49000);
        Assert.Equal(0L, result.CreatorFundVnd);
    }

    [Fact]
    public async Task GetAsync_CountsBuyingAndRepeatBuyingParents()
    {
        Paid(ReadingPlanId, 1, MidPeriod);
        Paid(AddOnPlanId, 1, MidPeriod.AddDays(1));           // phụ huynh 1: hai lần trong kỳ
        Paid(ReadingPlanId, 2, PeriodStart.AddDays(-20));     // phụ huynh 2: một lần trước kỳ
        Paid(ReadingPlanId, 2, MidPeriod);                    //              và một lần trong kỳ
        Paid(CreativePlanId, 3, MidPeriod);                   // phụ huynh 3: lần đầu
        Paid(CreativePlanId, 3, PeriodEnd.AddDays(5));        //              lần hai sau kỳ: chưa tính là mua lại
        Paid(CreativePlanId, 4, PeriodStart.AddDays(-40));    // phụ huynh 4: chỉ mua trước kỳ
        Paid(CreativePlanId, 4, PeriodStart.AddDays(-10));
        Transaction(CreativePlanId, 5, PaymentStatus.Pending, MidPeriod);   // chưa trả thì không phải người mua

        var result = await _sut.GetAsync(PeriodStart, PeriodEnd);

        Assert.Equal((3, 2), (result.BuyingParents, result.RepeatBuyingParents));
    }

    [Fact]
    public async Task GetAsync_AdminMarkedPaid_IsIncludedInRevenueAndCountedSeparately()
    {
        Paid(CreativePlanId, 1, MidPeriod);
        MarkedPaidByAdmin(Paid(AddOnPlanId, 2, MidPeriod));
        MarkedPaidByAdmin(Paid(ReadingPlanId, 3, PeriodStart.AddDays(-3)));   // xác nhận tay nhưng ngoài kỳ

        var result = await _sut.GetAsync(PeriodStart, PeriodEnd);

        Assert.Equal(128000L, result.TotalRevenueVnd);
        Assert.Equal((1, 29000L), (result.ManuallyMarkedPaidCount, result.ManuallyMarkedPaidRevenueVnd));
    }

    [Fact]
    public async Task GetAsync_ReportsCreditsGrantedAndUsedInThePeriod()
    {
        Lot(CreditSource.Cycle, 4, MidPeriod);
        Lot(CreditSource.Cycle, 4, MidPeriod.AddDays(3));
        Lot(CreditSource.AddOn, 2, MidPeriod);
        Lot(CreditSource.Trial, 1, PeriodStart);
        Lot(CreditSource.AdminAdjust, 3, PeriodEnd.AddHours(22));
        Lot(CreditSource.Cycle, 4, PeriodStart.AddDays(-1));
        Lot(CreditSource.Cycle, 4, PeriodEnd.AddDays(1));
        Usage(1, CreditUsageReason.StoryRequest, MidPeriod);
        Usage(1, CreditUsageReason.StoryRequest, PeriodEnd.AddHours(23));
        Usage(2, CreditUsageReason.AdminDeduction, MidPeriod);
        Usage(1, CreditUsageReason.StoryRequest, PeriodStart.AddMinutes(-1));
        Usage(1, CreditUsageReason.StoryRequest, PeriodEnd.AddDays(1));

        var result = await _sut.GetAsync(PeriodStart, PeriodEnd);

        Assert.Equal((14, 4), (result.CreditsGranted, result.CreditsUsed));
        Assert.Equal(
            ["Trial:1", "Cycle:8", "AddOn:2", "AdminAdjust:3"],
            result.CreditsGrantedBySource.Select(row => $"{row.Source}:{row.Credits}"));
    }

    [Theory]
    [InlineData(49000, 99000, 24600)]   // 14.700 + 9.900
    [InlineData(33333, 0, 9999)]        // 9.999,9 làm tròn xuống
    [InlineData(0, 11111, 1111)]        // 1.111,1 làm tròn xuống
    [InlineData(0, 0, 0)]
    public async Task GetAsync_CreatorFund_Is30PercentOfReadingPlus10PercentOfCreative_RoundedDown(
        int readingAmount, int creativeAmount, long expectedFund)
    {
        if (readingAmount > 0) Paid(ReadingPlanId, 1, MidPeriod, readingAmount);
        if (creativeAmount > 0) Paid(CreativePlanId, 2, MidPeriod, creativeAmount);
        Paid(AddOnPlanId, 3, MidPeriod);                                    // Add-on không vào quỹ
        Transaction(ReadingPlanId, 4, PaymentStatus.MismatchAmount, MidPeriod);   // lệch tiền không vào quỹ

        var result = await _sut.GetAsync(PeriodStart, PeriodEnd);

        Assert.Equal(expectedFund, result.CreatorFundVnd);
        Assert.Equal((0.30m, 0.10m), (result.ReadingFundRate, result.CreativeFundRate));
    }

    [Fact]
    public async Task GetAsync_CreatorPackRevenue_IsReportedButNotCountedAsParentsBuyingOrInTheCreatorFund()
    {
        _store.Seed(new SubscriptionPlan { Id = 5, Name = "Gói Creator", PlanType = PlanType.CreatorPack, Audience = PlanAudience.Creator, PriceVnd = 99000, AiCredits = 6, SubmitCredits = 20 });
        Paid(ReadingPlanId, 1, MidPeriod);
        Paid(5, 50, MidPeriod);
        Paid(5, 50, MidPeriod.AddDays(1));

        var result = await _sut.GetAsync(PeriodStart, PeriodEnd);

        Assert.Equal((247000L, 3), (result.TotalRevenueVnd, result.PaidTransactionCount));
        Assert.Contains(result.RevenueByPlanType, row => row.PlanType == "CreatorPack" && row.TransactionCount == 2 && row.RevenueVnd == 198000);
        Assert.Equal((1, 0), (result.BuyingParents, result.RepeatBuyingParents));
        Assert.Equal(14700L, result.CreatorFundVnd);
    }

    [Fact]
    public async Task GetAsync_CreditsGrantedAndUsed_CountAiCreditsOnly()
    {
        Lot(CreditSource.CreatorMonthly, 2, MidPeriod);
        _store.Seed(new CreditLot { UserId = 50, CreditType = CreditType.Submit, Source = CreditSource.CreatorMonthly, Total = 5, Remaining = 5, CreatedAt = MidPeriod });
        Usage(1, CreditUsageReason.StoryRequest, MidPeriod);
        Usage(1, CreditUsageReason.PublicSubmission, MidPeriod);

        var result = await _sut.GetAsync(PeriodStart, PeriodEnd);

        Assert.Equal((2, 1), (result.CreditsGranted, result.CreditsUsed));
    }

    [Fact]
    public async Task GetAsync_EmptyPeriod_ReturnsZeroesWithEveryPlanTypeListed()
    {
        var result = await _sut.GetAsync(PeriodStart, PeriodEnd);

        Assert.Equal((0L, 0, 0, 0, 0, 0, 0L), (
            result.TotalRevenueVnd, result.PaidTransactionCount, result.BuyingParents, result.RepeatBuyingParents,
            result.CreditsGranted, result.CreditsUsed, result.CreatorFundVnd));
        Assert.Empty(result.RevenueByPlan);
        Assert.Equal(
            ["Reading:0:0", "Creative:0:0", "AddOn:0:0", "CreatorPack:0:0"],
            result.RevenueByPlanType.Select(row => $"{row.PlanType}:{row.TransactionCount}:{row.RevenueVnd}"));
    }

    [Fact]
    public async Task GetAsync_SingleDayPeriod_CoversThatWholeDay()
    {
        Paid(ReadingPlanId, 1, MidPeriod.Date.AddHours(23));
        Paid(ReadingPlanId, 2, MidPeriod.Date.AddDays(1));

        var result = await _sut.GetAsync(MidPeriod, MidPeriod);

        Assert.Equal(1, result.PaidTransactionCount);
    }

    [Fact]
    public async Task GetAsync_PeriodEndBeforeStart_ThrowsBadRequest()
    {
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.GetAsync(PeriodEnd, PeriodStart));
    }

    [Fact]
    public async Task GetAsync_UsesConfiguredFundRates()
    {
        _store.Seed(new PlanSettings { Id = PlanSettings.SingletonId, ReadingFundPercent = 20, CreativeFundPercent = 5 });
        Paid(ReadingPlanId, 1, MidPeriod, 100000);
        Paid(CreativePlanId, 2, MidPeriod, 100000);

        var result = await new RevenueStatisticsService(_store, new PlanSettingsProvider(_store)).GetAsync(PeriodStart, PeriodEnd);

        Assert.Equal((25000L, 0.20m, 0.05m), (result.CreatorFundVnd, result.ReadingFundRate, result.CreativeFundRate));
    }
}
