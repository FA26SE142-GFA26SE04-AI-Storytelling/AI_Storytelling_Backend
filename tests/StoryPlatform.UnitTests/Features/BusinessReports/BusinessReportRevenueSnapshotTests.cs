using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.BusinessReports.DTOs;
using StoryPlatform.Application.Features.BusinessReports.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.UnitTests.Features.TokenQuota;
using Xunit;

namespace StoryPlatform.UnitTests.Features.BusinessReports;

/// <summary>Báo cáo kỳ với dịch vụ doanh thu thật: kỳ không chồng nhau, số liệu được chụp lúc tạo.</summary>
public class BusinessReportRevenueSnapshotTests
{
    private const int AdminId = 9;
    private const int CreativePlanId = 3;

    private static readonly DateTime November1 = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime November30 = new(2026, 11, 30, 0, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryUnitOfWork _store = new();
    private readonly Mock<IAuditLogWriter> _auditLogWriter = new();
    private readonly BusinessReportService _sut;

    public BusinessReportRevenueSnapshotTests()
    {
        _store.Seed(new SubscriptionPlan { Id = 2, Name = "Gói Đọc", PlanType = PlanType.Reading, PriceVnd = 49000 });
        _store.Seed(new SubscriptionPlan { Id = CreativePlanId, Name = "Gói Sáng tạo", PlanType = PlanType.Creative, PriceVnd = 99000, AiCredits = 4 });
        _sut = new BusinessReportService(_store, _auditLogWriter.Object, new RevenueStatisticsService(_store));
    }

    private static GenerateBusinessReportRequestDto Period(DateTime start, DateTime end) =>
        new() { PeriodStart = start, PeriodEnd = end };

    private PaymentTransaction SeedPaid(DateTime paidAt, int payerUserId = 1) => _store.Seed(new PaymentTransaction
    {
        PlanId = CreativePlanId, PayerUserId = payerUserId, TransactionCode = $"SEPAYSNAP{paidAt.Ticks % 100000:D5}{payerUserId}",
        Amount = 99000, Status = PaymentStatus.Paid, PaidAt = paidAt, CreatedAt = paidAt.AddMinutes(-2),
        ExpiresAt = paidAt.AddMinutes(13)
    });

    [Fact]
    public async Task GenerateAsync_SnapshotsRevenueOfThePeriod_IncludingItsLastDay()
    {
        SeedPaid(November1.AddDays(9));
        SeedPaid(November30.AddHours(21), payerUserId: 2);
        SeedPaid(November30.AddDays(1), payerUserId: 3);   // 01/12: ngoài kỳ

        var result = await _sut.GenerateAsync(AdminId, Period(November1, November30));

        Assert.Equal(("Compiling", November1, November30), (result.Status, result.PeriodStart, result.PeriodEnd));
        Assert.Equal((198000L, 2, 19800L), (result.Revenue!.TotalRevenueVnd, result.Revenue.PaidTransactionCount, result.Revenue.CreatorFundVnd));
        _auditLogWriter.Verify(
            writer => writer.LogAsync(
                AdminId, "BusinessReportGenerated", nameof(BusinessReport), result.Id,
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PublishedReport_KeepsItsSnapshot_WhenLaterPaymentsFallInThePeriod()
    {
        SeedPaid(November1.AddDays(9));
        var generated = await _sut.GenerateAsync(AdminId, Period(November1, November30));
        await _sut.PublishAsync(AdminId, generated.Id);

        SeedPaid(November1.AddDays(20), payerUserId: 2);   // ví dụ Admin xác nhận tay một giao dịch cũ sau đó

        var stored = await _sut.GetByIdAsync(generated.Id);
        Assert.Equal(("Published", 99000L), (stored.Status, stored.Revenue!.TotalRevenueVnd));
        var live = await new RevenueStatisticsService(_store).GetAsync(November1, November30);
        Assert.Equal(198000L, live.TotalRevenueVnd);
    }

    [Theory]
    [InlineData("2026-11-01", "2026-11-30")]   // trùng khít
    [InlineData("2026-11-15", "2026-12-15")]   // chồng phần cuối
    [InlineData("2026-10-15", "2026-11-01")]   // chạm đúng ngày đầu kỳ đã có
    [InlineData("2026-11-30", "2026-12-31")]   // bắt đầu vào ngày cuối kỳ đã có
    [InlineData("2026-11-10", "2026-11-12")]   // nằm trọn bên trong
    [InlineData("2026-10-01", "2026-12-31")]   // bao trùm
    public async Task GenerateAsync_OverlappingAnExistingReport_ThrowsConflict(string start, string end)
    {
        await _sut.GenerateAsync(AdminId, Period(November1, November30));

        await Assert.ThrowsAsync<ConflictException>(() => _sut.GenerateAsync(AdminId, Period(Utc(start), Utc(end))));

        Assert.Single(_store.Items<BusinessReport>());
    }

    [Theory]
    [InlineData("2026-12-01", "2026-12-31")]   // kỳ kế tiếp bắt đầu vào ngày sau
    [InlineData("2026-10-01", "2026-10-31")]   // kỳ liền trước
    public async Task GenerateAsync_AdjacentPeriod_IsAllowed(string start, string end)
    {
        await _sut.GenerateAsync(AdminId, Period(November1, November30));

        await _sut.GenerateAsync(AdminId, Period(Utc(start), Utc(end)));

        Assert.Equal(2, _store.Items<BusinessReport>().Count);
    }

    [Fact]
    public async Task GenerateAsync_PeriodEndBeforeStart_ThrowsBadRequest()
    {
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.GenerateAsync(AdminId, Period(November30, November1)));
    }

    [Fact]
    public async Task PublishAsync_AlreadyPublished_ThrowsConflict()
    {
        var generated = await _sut.GenerateAsync(AdminId, Period(November1, November30));
        await _sut.PublishAsync(AdminId, generated.Id);

        await Assert.ThrowsAsync<ConflictException>(() => _sut.PublishAsync(AdminId, generated.Id));
    }

    [Fact]
    public async Task GetLatestPublishedAsync_DoesNotExposeRevenue()
    {
        SeedPaid(November1.AddDays(9));
        var generated = await _sut.GenerateAsync(AdminId, Period(November1, November30));
        await _sut.PublishAsync(AdminId, generated.Id);

        var result = await _sut.GetLatestPublishedAsync();

        Assert.IsNotType<AdminBusinessReportDto>(result);
        Assert.DoesNotContain("revenue", System.Text.Json.JsonSerializer.Serialize<object>(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListAsync_ReturnsNewestPeriodFirst_AndLegacyReportsWithoutRevenue()
    {
        _store.Seed(new BusinessReport
        {
            PeriodStart = Utc("2026-09-01"), PeriodEnd = Utc("2026-09-30"), StoriesGenerated = 7, Status = ReportStatus.Published
        });
        await _sut.GenerateAsync(AdminId, Period(November1, November30));
        await _sut.GenerateAsync(AdminId, Period(Utc("2026-10-01"), Utc("2026-10-31")));

        var result = await _sut.ListAsync(pageIndex: 1, pageSize: 20);

        Assert.Equal(
            [November1, Utc("2026-10-01"), Utc("2026-09-01")],
            result.Items.Select(item => item.PeriodStart));
        Assert.NotNull(result.Items[0].Revenue);
        Assert.Null(result.Items[2].Revenue);
        Assert.Equal(7, result.Items[2].StoriesGenerated);
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetByIdAsync(999));
    }

    private static DateTime Utc(string date) =>
        DateTime.SpecifyKind(DateTime.Parse(date, System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc);
}
