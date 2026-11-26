using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Time;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.CreatorPayouts.DTOs;
using StoryPlatform.Application.Features.CreatorPayouts.Services;
using StoryPlatform.Application.Features.CreatorStatistics.DTOs;
using StoryPlatform.Application.Features.CreatorStatistics.Interfaces;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.UnitTests.Features.TokenQuota;
using Xunit;

namespace StoryPlatform.UnitTests.Features.CreatorPayouts;

/// <summary>Chi trả cho Creator (Luồng 5, 5h).</summary>
public sealed class CreatorPayoutServiceTests
{
    private const int AdminId = 1;
    private const int CreatorA = 2;
    private const int CreatorB = 3;
    private const int CreatorC = 4;

    private readonly InMemoryUnitOfWork _store = new();
    private readonly Mock<IAuditLogWriter> _audit = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly Mock<ICreatorStatisticsService> _statistics = new();
    private readonly CreatorPayoutService _sut;

    public CreatorPayoutServiceTests()
    {
        _store.Seed(new UserAccount { Id = AdminId, Role = UserRole.Administrator, Status = AccountStatus.Active });
        _sut = new CreatorPayoutService(_store, _statistics.Object, _audit.Object, _notifications.Object);
    }

    private static DateTime Today => VietnamTime.LocalDate(DateTime.UtcNow);

    private void SeedCreator(int id, long balance, bool withAccount = true, AccountStatus status = AccountStatus.Active)
    {
        _store.Seed(new UserAccount
        {
            Id = id, Username = $"creator_{id}", FullName = $"Creator {id}", Role = UserRole.ContentCreator, Status = status
        });
        if (withAccount)
        {
            _store.Seed(new CreatorPayoutAccount
            {
                Id = id, CreatorUserId = id, BankName = "Vietcombank", AccountNumber = $"00110000{id:D4}",
                AccountHolderName = $"CREATOR {id}"
            });
        }

        if (balance != 0)
        {
            _store.Seed(new CreatorBalanceEntry
            {
                Id = 100 + id, CreatorUserId = id, Type = CreatorBalanceEntryType.PeriodEarning, AmountVnd = balance,
                ValidReads = 10
            });
        }
    }

    private static MarkCreatorPayoutPaidRequestDto Paid(long amount, string code = "FT26321123456") =>
        new() { AmountVnd = amount, BankTransactionCode = code, TransferredOn = Today };

    private long BalanceOf(int creatorId) =>
        _store.Items<CreatorBalanceEntry>().Where(entry => entry.CreatorUserId == creatorId).Sum(entry => entry.AmountVnd);

    // ---------- Danh sách chi ----------

    private void SetMinimumPayout(long minimum) =>
        _store.Seed(new PlanSettings { Id = PlanSettings.SingletonId, MinimumPayoutVnd = minimum });

    [Fact]
    public async Task GetDue_DefaultMinimum_ListsOnlyActiveCreatorsWithBalanceFrom200kAndPayoutAccount()
    {
        SeedCreator(CreatorA, 655_000);                                       // AC1
        SeedCreator(CreatorB, 300_000, withAccount: false);                   // AC4: chưa nhập thông tin
        SeedCreator(CreatorC, 78_600);                                        // AC2: dưới mức chi mặc định
        SeedCreator(5, 500_000, status: AccountStatus.Suspended);             // bị vô hiệu hoá
        SeedCreator(6, 200_000);                                              // đúng mức tối thiểu

        var due = await _sut.GetDueAsync();

        Assert.Equal([CreatorA, 6], due.Select(row => row.CreatorUserId));
        var a = due[0];
        Assert.Equal(655_000, a.BalanceVnd);
        Assert.Equal("Vietcombank", a.BankName);
        Assert.Equal("001100000002", a.AccountNumber);
        Assert.Equal("CREATOR 2", a.AccountHolderName);
    }

    [Fact]
    public async Task GetOverview_WithoutPayoutAccount_ShowsReminderAndKeepsBalance()
    {
        SeedCreator(CreatorB, 300_000, withAccount: false);

        var overview = await _sut.GetOverviewAsync(CreatorB);

        Assert.Equal(300_000, overview.BalanceVnd);
        Assert.False(overview.HasPayoutAccount);
        Assert.NotNull(overview.Reminder);
        Assert.Null(overview.PayoutAccount);
        Assert.Equal(200_000, overview.MinimumPayoutVnd);
    }

    [Theory]
    [InlineData(0L, new[] { CreatorA, CreatorC })]          // 0 = không có mức tối thiểu, số dư 0 vẫn không được chi
    [InlineData(50_000L, new[] { CreatorA, CreatorC })]
    [InlineData(700_000L, new int[0])]
    public async Task GetDue_UsesTheMinimumAdminSet(long minimum, int[] expected)
    {
        SetMinimumPayout(minimum);
        SeedCreator(CreatorA, 655_000);
        SeedCreator(CreatorC, 78_600);
        SeedCreator(6, 0);

        var due = await _sut.GetDueAsync();

        Assert.Equal(expected, due.Select(row => row.CreatorUserId));
        Assert.Equal(minimum, (await _sut.GetOverviewAsync(CreatorA)).MinimumPayoutVnd);
    }

    [Fact]
    public async Task GetOverview_NonCreator_IsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetOverviewAsync(AdminId));
    }

    // ---------- Thông tin nhận tiền ----------

    [Fact]
    public async Task UpdatePayoutAccount_CreatesThenUpdates_AndAuditsMaskedNumber()
    {
        SeedCreator(CreatorB, 0, withAccount: false);

        await _sut.UpdatePayoutAccountAsync(CreatorB, new UpdateCreatorPayoutAccountRequestDto
        {
            BankName = " Techcombank ", AccountNumber = "1903 6612 3456 78", AccountHolderName = " NGUYEN VAN B "
        });
        var updated = await _sut.UpdatePayoutAccountAsync(CreatorB, new UpdateCreatorPayoutAccountRequestDto
        {
            BankName = "ACB", AccountNumber = "987654321", AccountHolderName = "NGUYEN VAN B"
        });

        var account = Assert.Single(_store.Items<CreatorPayoutAccount>());
        Assert.Equal("ACB", account.BankName);
        Assert.Equal("987654321", account.AccountNumber);
        Assert.Equal("987654321", updated.AccountNumber);
        _audit.Verify(a => a.LogAsync(CreatorB, "CreatorPayoutAccountUpdated", nameof(CreatorPayoutAccount), account.Id,
            null, It.Is<object>(state => state.ToString()!.Contains("**********5678")), It.IsAny<CancellationToken>()), Times.Once);
        _audit.Verify(a => a.LogAsync(CreatorB, "CreatorPayoutAccountUpdated", nameof(CreatorPayoutAccount), account.Id,
            It.Is<object>(state => state != null && state.ToString()!.Contains("Techcombank")),
            It.Is<object>(state => state.ToString()!.Contains("*****4321")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("V", "123456789", "NGUYEN VAN B")]
    [InlineData("Vietcombank", "12345", "NGUYEN VAN B")]
    [InlineData("Vietcombank", "12345abc90", "NGUYEN VAN B")]
    [InlineData("Vietcombank", "123456789", " ")]
    public async Task UpdatePayoutAccount_InvalidInput_IsBadRequest(string bank, string number, string holder)
    {
        SeedCreator(CreatorB, 0, withAccount: false);

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.UpdatePayoutAccountAsync(CreatorB,
            new UpdateCreatorPayoutAccountRequestDto { BankName = bank, AccountNumber = number, AccountHolderName = holder }));
        Assert.Empty(_store.Items<CreatorPayoutAccount>());
    }

    // ---------- Đánh dấu Đã chi ----------

    [Fact]
    public async Task MarkPaid_RecordsPayoutZeroesBalanceNotifiesAndAudits()
    {
        SeedCreator(CreatorA, 655_000);

        var payout = await _sut.MarkPaidAsync(AdminId, CreatorA, Paid(655_000));

        Assert.Equal(0, BalanceOf(CreatorA));                                 // AC3
        Assert.Equal(655_000, payout.AmountVnd);
        Assert.Equal("Paid", payout.Status);
        Assert.Equal("FT26321123456", payout.BankTransactionCode);
        Assert.Equal("001100000002", payout.AccountNumber);
        Assert.Contains($"lock:{CreatorA}", _store.Calls);
        _notifications.Verify(n => n.CreateAsync(CreatorA, NotificationType.CreatorPayoutPaid,
            It.Is<string>(json => json.Contains("655000")), It.IsAny<CancellationToken>()), Times.Once);
        _audit.Verify(a => a.LogAsync(AdminId, "CreatorPayoutMarkedPaid", nameof(CreatorBalanceEntry), payout.PayoutId,
            It.IsAny<object>(), It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);

        var overview = await _sut.GetOverviewAsync(CreatorA);
        var history = Assert.Single(overview.Payouts);
        Assert.Equal(655_000, history.AmountVnd);
        Assert.Equal(0, overview.BalanceVnd);
        Assert.Empty(await _sut.GetDueAsync());
    }

    [Fact]
    public async Task MarkPaid_SecondTimeForSameBalance_IsRejectedWithoutNewPayout()
    {
        SeedCreator(CreatorA, 655_000);
        await _sut.MarkPaidAsync(AdminId, CreatorA, Paid(655_000));

        var error = await Assert.ThrowsAsync<ConflictException>(() => _sut.MarkPaidAsync(AdminId, CreatorA, Paid(655_000)));

        Assert.Contains("về 0", error.Message);
        Assert.Single(_store.Items<CreatorBalanceEntry>(), entry => entry.Type == CreatorBalanceEntryType.Payout);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MarkPaid_WithoutTransactionCode_IsBadRequest(string code)
    {
        SeedCreator(CreatorA, 655_000);

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.MarkPaidAsync(AdminId, CreatorA, Paid(655_000, code)));
        Assert.Equal(655_000, BalanceOf(CreatorA));
    }

    [Fact]
    public async Task MarkPaid_WithoutOrFutureTransferDate_IsBadRequest()
    {
        SeedCreator(CreatorA, 655_000);

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.MarkPaidAsync(AdminId, CreatorA,
            new MarkCreatorPayoutPaidRequestDto { AmountVnd = 655_000, BankTransactionCode = "FT1" }));
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.MarkPaidAsync(AdminId, CreatorA,
            new MarkCreatorPayoutPaidRequestDto { AmountVnd = 655_000, BankTransactionCode = "FT1", TransferredOn = Today.AddDays(1) }));
    }

    [Fact]
    public async Task MarkPaid_AmountDifferentFromBalance_IsConflict()
    {
        SeedCreator(CreatorA, 655_000);

        var error = await Assert.ThrowsAsync<ConflictException>(() => _sut.MarkPaidAsync(AdminId, CreatorA, Paid(600_000)));

        Assert.Contains("655,000", error.Message.Replace('.', ','));
        Assert.Equal(655_000, BalanceOf(CreatorA));
    }

    [Fact]
    public async Task MarkPaid_BelowTheMinimumAdminSet_IsConflict_ThenPaidOnceTheMinimumIsLowered()
    {
        SeedCreator(CreatorC, 78_600);

        var error = await Assert.ThrowsAsync<ConflictException>(() => _sut.MarkPaidAsync(AdminId, CreatorC, Paid(78_600)));
        Assert.Contains("200", error.Message);
        Assert.Equal(78_600, BalanceOf(CreatorC));

        SetMinimumPayout(0);
        await _sut.MarkPaidAsync(AdminId, CreatorC, Paid(78_600));

        Assert.Equal(0, BalanceOf(CreatorC));
    }

    [Fact]
    public async Task MarkPaid_ZeroBalanceWithoutAccountOrSuspended_IsConflict()
    {
        SetMinimumPayout(0);
        SeedCreator(6, 0);
        SeedCreator(CreatorB, 300_000, withAccount: false);
        SeedCreator(5, 500_000, status: AccountStatus.Suspended);

        await Assert.ThrowsAsync<ConflictException>(() => _sut.MarkPaidAsync(AdminId, 6, Paid(0)));
        await Assert.ThrowsAsync<ConflictException>(() => _sut.MarkPaidAsync(AdminId, CreatorB, Paid(300_000)));
        await Assert.ThrowsAsync<ConflictException>(() => _sut.MarkPaidAsync(AdminId, 5, Paid(500_000)));
        Assert.DoesNotContain(_store.Items<CreatorBalanceEntry>(), entry => entry.Type == CreatorBalanceEntryType.Payout);
    }

    [Fact]
    public async Task MarkPaid_ByNonAdmin_IsForbidden()
    {
        SeedCreator(CreatorA, 655_000);

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.MarkPaidAsync(CreatorA, CreatorA, Paid(655_000)));
    }

    // ---------- Huỷ lần chi ----------

    [Fact]
    public async Task CancelPayout_RestoresBalanceOnce_AndShowsCancelledInHistory()
    {
        SeedCreator(CreatorA, 655_000);
        var payout = await _sut.MarkPaidAsync(AdminId, CreatorA, Paid(655_000));

        var cancelled = await _sut.CancelPayoutAsync(AdminId, payout.PayoutId,
            new CancelCreatorPayoutRequestDto { Reason = "Ngân hàng trả lại tiền do sai tên chủ tài khoản" });

        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Equal(655_000, BalanceOf(CreatorA));
        await Assert.ThrowsAsync<ConflictException>(() => _sut.CancelPayoutAsync(AdminId, payout.PayoutId,
            new CancelCreatorPayoutRequestDto { Reason = "lần nữa" }));
        Assert.Equal(655_000, BalanceOf(CreatorA));

        var history = Assert.Single((await _sut.GetOverviewAsync(CreatorA)).Payouts);
        Assert.Equal("Cancelled", history.Status);
        Assert.Equal("Ngân hàng trả lại tiền do sai tên chủ tài khoản", history.CancelReason);
        _audit.Verify(a => a.LogAsync(AdminId, "CreatorPayoutCancelled", nameof(CreatorBalanceEntry), payout.PayoutId,
            It.IsAny<object>(), It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(n => n.CreateAsync(CreatorA, NotificationType.CreatorPayoutCancelled,
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);

        // Số dư được cộng lại nên Admin chi lại được.
        Assert.Single(await _sut.GetDueAsync());
    }

    [Fact]
    public async Task CancelPayout_WithoutReasonOrUnknownPayout_IsRejected()
    {
        SeedCreator(CreatorA, 655_000);
        var payout = await _sut.MarkPaidAsync(AdminId, CreatorA, Paid(655_000));

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.CancelPayoutAsync(AdminId, payout.PayoutId,
            new CancelCreatorPayoutRequestDto { Reason = " " }));
        // Bút toán thu nhập không phải lần chi.
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.CancelPayoutAsync(AdminId, 100 + CreatorA,
            new CancelCreatorPayoutRequestDto { Reason = "Sai" }));
        Assert.Equal(0, BalanceOf(CreatorA));
    }

    // ---------- Chốt kỳ ----------

    private void SetupEarnings(params (int Creator, int Reads, long Earnings)[] rows) =>
        _statistics.Setup(s => s.GetPeriodEarningsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatorPeriodEarningsDto
            {
                CreatorFundVnd = 1_000_000,
                TotalValidReads = rows.Sum(row => row.Reads),
                Creators = rows.Select(row => new CreatorPeriodEarningDto
                {
                    CreatorUserId = row.Creator, ValidReads = row.Reads, EarningsVnd = row.Earnings
                }).ToList()
            });

    [Fact]
    public async Task CloseDuePeriod_BeforeDay8_DoesNothing()
    {
        // 23:00 UTC ngày 6/12 = 06:00 ngày 7/12 giờ Việt Nam.
        var closed = await _sut.CloseDuePeriodAsync(new DateTime(2026, 12, 6, 23, 0, 0, DateTimeKind.Utc));

        Assert.False(closed);
        Assert.Empty(_store.Items<CreatorEarningPeriod>());
        _statistics.Verify(s => s.GetPeriodEarningsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CloseDuePeriod_FromDay8_CreditsPreviousMonthOnce_AndAccumulatesWithOldBalance()
    {
        SeedCreator(CreatorA, 0);
        SeedCreator(CreatorC, 78_600);                                        // số dư cũ dưới mức chi
        SeedCreator(5, 0, status: AccountStatus.Suspended);
        SetupEarnings((CreatorA, 60, 655_000), (CreatorC, 20, 150_000), (5, 5, 40_000), (6, 1, 0));
        // 17:00 UTC ngày 7/12 = 00:00 ngày 8/12 giờ Việt Nam.
        var now = new DateTime(2026, 12, 7, 17, 0, 0, DateTimeKind.Utc);

        Assert.True(await _sut.CloseDuePeriodAsync(now));
        Assert.False(await _sut.CloseDuePeriodAsync(now.AddHours(1)));

        _statistics.Verify(s => s.GetPeriodEarningsAsync(
            new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 30, 0, 0, 0, DateTimeKind.Utc),
            It.IsAny<CancellationToken>()), Times.Once);
        var period = Assert.Single(_store.Items<CreatorEarningPeriod>());
        Assert.Equal(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), period.PeriodMonth);
        Assert.Equal(845_000, period.DistributedVnd);
        Assert.Equal(655_000, BalanceOf(CreatorA));
        Assert.Equal(228_600, BalanceOf(CreatorC));                           // cộng dồn sang kỳ sau
        Assert.Equal(40_000, BalanceOf(5));                                   // bị vô hiệu hoá vẫn giữ số dư
        Assert.DoesNotContain(_store.Items<CreatorBalanceEntry>(), entry => entry.CreatorUserId == 6);
        Assert.Contains($"lock:{int.MinValue}", _store.Calls);

        var earnings = (await _sut.GetOverviewAsync(CreatorA)).Earnings;
        var earning = Assert.Single(earnings);
        Assert.Equal(period.PeriodMonth, earning.PeriodMonth);
        Assert.Equal(60, earning.ValidReads);
        Assert.Equal([CreatorA, CreatorC], (await _sut.GetDueAsync()).Select(row => row.CreatorUserId));
    }

    [Theory]
    [InlineData(1, 1, true)]     // ngày chốt 1: chốt tháng 11 ngay ngày 1/12
    [InlineData(15, 14, false)]  // ngày chốt 15: ngày 14/12 chưa chốt
    [InlineData(15, 15, true)]
    public async Task CloseDuePeriod_UsesTheCloseDayAdminSet(int closeDay, int vietnamDay, bool expected)
    {
        _store.Seed(new PlanSettings { Id = PlanSettings.SingletonId, CreatorPeriodCloseDay = closeDay });
        SeedCreator(CreatorA, 0);
        SetupEarnings((CreatorA, 10, 69_000));
        // 03:00 UTC = 10:00 giờ Việt Nam cùng ngày.
        var now = new DateTime(2026, 12, vietnamDay, 3, 0, 0, DateTimeKind.Utc);

        Assert.Equal(expected, await _sut.CloseDuePeriodAsync(now));

        Assert.Equal(expected ? 69_000 : 0, BalanceOf(CreatorA));
        Assert.Equal(closeDay, (await _sut.GetOverviewAsync(CreatorA)).PeriodCloseDay);
    }

    [Fact]
    public async Task CloseDuePeriod_January_ClosesDecemberOfPreviousYear()
    {
        SetupEarnings();

        Assert.True(await _sut.CloseDuePeriodAsync(new DateTime(2027, 1, 10, 3, 0, 0, DateTimeKind.Utc)));

        Assert.Equal(new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc), Assert.Single(_store.Items<CreatorEarningPeriod>()).PeriodMonth);
    }

    [Fact]
    public void Mask_KeepsLastFourDigits()
    {
        Assert.Equal("*****4321", CreatorPayoutService.Mask("987654321"));
        Assert.Equal("1234", CreatorPayoutService.Mask("1234"));
    }
}
