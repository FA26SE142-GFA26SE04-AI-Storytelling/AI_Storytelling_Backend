using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StoryPlatform.Application.Features.Notifications.DTOs;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Application.Features.Payments.Services;
using StoryPlatform.Application.Features.TokenQuota.Interfaces;
using StoryPlatform.Application.Features.TokenQuota.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.UnitTests.Features.TokenQuota;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Payments;

public class SubscriptionServiceTests
{
    private const int UserId = 1;
    private const int ReadingPlanId = 2;
    private const int CreativePlanId = 3;
    private const int AddOnPlanId = 4;

    private static readonly DateTime Now = new(2026, 11, 10, 3, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryUnitOfWork _store = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly SubscriptionService _sut;
    private int _nextPaymentId = 100;

    public SubscriptionServiceTests()
    {
        _store.Seed(new UserAccount { Id = UserId, Role = UserRole.User, Status = AccountStatus.Active });
        _store.Seed(new SubscriptionPlan { Id = 1, Name = "Personal", PlanType = PlanType.Legacy, PriceVnd = 49000, AiCredits = 50, IsActive = false });
        _store.Seed(new SubscriptionPlan { Id = ReadingPlanId, Name = "Gói Đọc", PlanType = PlanType.Reading, PriceVnd = 49000, AiCredits = 0 });
        _store.Seed(new SubscriptionPlan { Id = CreativePlanId, Name = "Gói Sáng tạo", PlanType = PlanType.Creative, PriceVnd = 99000, AiCredits = 4 });
        _store.Seed(new SubscriptionPlan { Id = AddOnPlanId, Name = "Add-on", PlanType = PlanType.AddOn, PriceVnd = 29000, AiCredits = 2 });
        _notifications
            .Setup(service => service.CreateAsync(
                It.IsAny<int>(), It.IsAny<NotificationType>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationDto());

        _sut = CreateSut(new TokenQuotaService(_store));
    }

    private SubscriptionService CreateSut(ITokenQuotaService quota) =>
        new(_store, quota, _notifications.Object, NullLogger<SubscriptionService>.Instance);

    private SubscriptionPlan Plan(int id) => _store.Items<SubscriptionPlan>().Single(plan => plan.Id == id);

    private PaymentTransaction PaidTransaction(int planId, DateTime? paidAt = null) => _store.Seed(new PaymentTransaction
    {
        Id = _nextPaymentId++,
        PlanId = planId,
        PayerUserId = UserId,
        Amount = Plan(planId).PriceVnd,
        Status = PaymentStatus.Paid,
        PaidAt = paidAt ?? Now
    });

    private UserSubscription SeedSubscription(int planId, SubscriptionStatus status, DateTime startAt, DateTime endAt) =>
        _store.Seed(new UserSubscription
        {
            UserId = UserId,
            PlanId = planId,
            Status = status,
            StartAt = startAt,
            EndAt = endAt,
            PaymentId = _nextPaymentId++
        });

    private CreditLot SeedLot(CreditSource source, int remaining, DateTime? expiresAt) => _store.Seed(new CreditLot
    {
        UserId = UserId,
        CreditType = CreditType.AI,
        Source = source,
        Total = remaining,
        Remaining = remaining,
        ExpiresAt = expiresAt
    });

    private IReadOnlyList<UserSubscription> Subscriptions => _store.Items<UserSubscription>();
    private IReadOnlyList<CreditLot> Lots => _store.Items<CreditLot>();

    // ---------- ApplyPaidTransactionAsync: Gói Đọc / Gói Sáng tạo ----------

    [Fact]
    public async Task Apply_Creative_NoCurrentPlan_StartsCycleAndGrantsCredits()
    {
        var transaction = PaidTransaction(CreativePlanId);

        await _sut.ApplyPaidTransactionAsync(transaction, Plan(CreativePlanId), Now);

        var subscription = Assert.Single(Subscriptions);
        Assert.Equal(
            (SubscriptionStatus.Active, Now, new DateTime(2026, 12, 10, 3, 0, 0, DateTimeKind.Utc), transaction.Id, CreativePlanId),
            (subscription.Status, subscription.StartAt, subscription.EndAt, subscription.PaymentId, subscription.PlanId));
        var lot = Assert.Single(Lots);
        Assert.Equal(
            (CreditSource.Cycle, 4, 4, (DateTime?)subscription.EndAt, (int?)transaction.Id),
            (lot.Source, lot.Total, lot.Remaining, lot.ExpiresAt, lot.PaymentId));
    }

    [Fact]
    public async Task Apply_Reading_NoCurrentPlan_StartsCycleWithoutCredits()
    {
        var transaction = PaidTransaction(ReadingPlanId);

        await _sut.ApplyPaidTransactionAsync(transaction, Plan(ReadingPlanId), Now);

        var subscription = Assert.Single(Subscriptions);
        Assert.Equal((SubscriptionStatus.Active, Now.AddMonths(1)), (subscription.Status, subscription.EndAt));
        Assert.Empty(Lots);
    }

    [Fact]
    public async Task Apply_UsesPaidAtAsCycleStart()
    {
        var paidAt = Now.AddMinutes(-7);
        var transaction = PaidTransaction(ReadingPlanId, paidAt);

        await _sut.ApplyPaidTransactionAsync(transaction, Plan(ReadingPlanId), Now);

        var subscription = Assert.Single(Subscriptions);
        Assert.Equal((paidAt, paidAt.AddMonths(1)), (subscription.StartAt, subscription.EndAt));
    }

    [Fact]
    public async Task Apply_Creative_WhileCreativeActive_SchedulesAfterCurrentEnd()
    {
        var currentEnd = Now.AddDays(5);
        var current = SeedSubscription(CreativePlanId, SubscriptionStatus.Active, currentEnd.AddMonths(-1), currentEnd);
        var cycleLot = SeedLot(CreditSource.Cycle, 2, currentEnd);
        var transaction = PaidTransaction(CreativePlanId);

        await _sut.ApplyPaidTransactionAsync(transaction, Plan(CreativePlanId), Now);

        var scheduled = Subscriptions.Single(item => item.PaymentId == transaction.Id);
        Assert.Equal(
            (SubscriptionStatus.Scheduled, currentEnd, currentEnd.AddMonths(1)),
            (scheduled.Status, scheduled.StartAt, scheduled.EndAt));
        Assert.Equal(SubscriptionStatus.Active, current.Status);
        Assert.Equal(2, Assert.Single(Lots).Remaining);
        Assert.Equal(currentEnd, cycleLot.ExpiresAt);
    }

    [Fact]
    public async Task Apply_Reading_WhileCreativeActive_SchedulesDowngradeAfterCurrentEnd()
    {
        var currentEnd = Now.AddDays(12);
        var current = SeedSubscription(CreativePlanId, SubscriptionStatus.Active, currentEnd.AddMonths(-1), currentEnd);
        var transaction = PaidTransaction(ReadingPlanId);

        await _sut.ApplyPaidTransactionAsync(transaction, Plan(ReadingPlanId), Now);

        var scheduled = Subscriptions.Single(item => item.PaymentId == transaction.Id);
        Assert.Equal(
            (SubscriptionStatus.Scheduled, ReadingPlanId, currentEnd, currentEnd.AddMonths(1)),
            (scheduled.Status, scheduled.PlanId, scheduled.StartAt, scheduled.EndAt));
        Assert.Equal(SubscriptionStatus.Active, current.Status);
    }

    [Fact]
    public async Task Apply_Renewal_WithExistingScheduled_AppendsToEndOfChain()
    {
        var firstEnd = Now.AddDays(5);
        var secondEnd = firstEnd.AddMonths(1);
        SeedSubscription(CreativePlanId, SubscriptionStatus.Active, firstEnd.AddMonths(-1), firstEnd);
        SeedSubscription(CreativePlanId, SubscriptionStatus.Scheduled, firstEnd, secondEnd);
        var transaction = PaidTransaction(CreativePlanId);

        await _sut.ApplyPaidTransactionAsync(transaction, Plan(CreativePlanId), Now);

        var appended = Subscriptions.Single(item => item.PaymentId == transaction.Id);
        Assert.Equal(
            (SubscriptionStatus.Scheduled, secondEnd, secondEnd.AddMonths(1)),
            (appended.Status, appended.StartAt, appended.EndAt));
    }

    [Fact]
    public async Task Apply_Creative_WhileReadingActive_UpgradesImmediately()
    {
        var readingEnd = Now.AddDays(10);
        var reading = SeedSubscription(ReadingPlanId, SubscriptionStatus.Active, readingEnd.AddMonths(-1), readingEnd);
        var addOnLot = SeedLot(CreditSource.AddOn, 1, readingEnd);
        var transaction = PaidTransaction(CreativePlanId);

        await _sut.ApplyPaidTransactionAsync(transaction, Plan(CreativePlanId), Now);

        Assert.Equal(SubscriptionStatus.Superseded, reading.Status);
        var creative = Subscriptions.Single(item => item.PaymentId == transaction.Id);
        Assert.Equal(
            (SubscriptionStatus.Active, Now, Now.AddMonths(1)),
            (creative.Status, creative.StartAt, creative.EndAt));
        var cycleLot = Lots.Single(lot => lot.Source == CreditSource.Cycle);
        Assert.Equal((4, (DateTime?)creative.EndAt), (cycleLot.Remaining, cycleLot.ExpiresAt));
        Assert.Equal((1, (DateTime?)creative.EndAt), (addOnLot.Remaining, addOnLot.ExpiresAt));
    }

    [Fact]
    public async Task Apply_Upgrade_DoesNotReviveExpiredAddOnLots()
    {
        var readingEnd = Now.AddDays(10);
        SeedSubscription(ReadingPlanId, SubscriptionStatus.Active, readingEnd.AddMonths(-1), readingEnd);
        var oldExpiry = Now.AddDays(-20);
        var staleAddOn = SeedLot(CreditSource.AddOn, 2, oldExpiry);

        await _sut.ApplyPaidTransactionAsync(PaidTransaction(CreativePlanId), Plan(CreativePlanId), Now);

        Assert.Equal(oldExpiry, staleAddOn.ExpiresAt);
    }

    [Fact]
    public async Task Apply_Upgrade_ShiftsExistingScheduledRowsAfterNewCycle()
    {
        var readingEnd = Now.AddDays(10);
        SeedSubscription(ReadingPlanId, SubscriptionStatus.Active, readingEnd.AddMonths(-1), readingEnd);
        var scheduled = SeedSubscription(ReadingPlanId, SubscriptionStatus.Scheduled, readingEnd, readingEnd.AddMonths(1));
        var transaction = PaidTransaction(CreativePlanId);

        await _sut.ApplyPaidTransactionAsync(transaction, Plan(CreativePlanId), Now);

        var creative = Subscriptions.Single(item => item.PaymentId == transaction.Id);
        Assert.Equal(
            (SubscriptionStatus.Scheduled, creative.EndAt, creative.EndAt.AddMonths(1)),
            (scheduled.Status, scheduled.StartAt, scheduled.EndAt));
    }

    [Fact]
    public async Task Apply_PaidOnJan31_EndsOnLastDayOfFebruary_AndRenewalStartsExactlyThere()
    {
        var paidAt = new DateTime(2027, 1, 31, 10, 0, 0, DateTimeKind.Utc);
        var first = PaidTransaction(ReadingPlanId, paidAt);
        await _sut.ApplyPaidTransactionAsync(first, Plan(ReadingPlanId), paidAt);
        var renewal = PaidTransaction(ReadingPlanId, paidAt.AddDays(1));

        await _sut.ApplyPaidTransactionAsync(renewal, Plan(ReadingPlanId), paidAt.AddDays(1));

        var februaryEnd = new DateTime(2027, 2, 28, 10, 0, 0, DateTimeKind.Utc);
        Assert.Equal(februaryEnd, Subscriptions.Single(item => item.PaymentId == first.Id).EndAt);
        var next = Subscriptions.Single(item => item.PaymentId == renewal.Id);
        Assert.Equal((februaryEnd, februaryEnd.AddMonths(1)), (next.StartAt, next.EndAt));
    }

    // ---------- ApplyPaidTransactionAsync: Add-on, Legacy ----------

    [Fact]
    public async Task Apply_AddOn_WithActivePlan_GrantsLotExpiringAtCycleEnd()
    {
        var cycleEnd = Now.AddDays(14);
        var reading = SeedSubscription(ReadingPlanId, SubscriptionStatus.Active, cycleEnd.AddMonths(-1), cycleEnd);
        var transaction = PaidTransaction(AddOnPlanId);

        await _sut.ApplyPaidTransactionAsync(transaction, Plan(AddOnPlanId), Now);

        var lot = Assert.Single(Lots);
        Assert.Equal(
            (CreditSource.AddOn, 2, (DateTime?)cycleEnd, (int?)transaction.Id),
            (lot.Source, lot.Remaining, lot.ExpiresAt, lot.PaymentId));
        Assert.Equal(cycleEnd, reading.EndAt);
        Assert.Single(Subscriptions);
    }

    [Fact]
    public async Task Apply_AddOn_NoActivePlan_GrantsLotValidOneMonthFromPaidAt()
    {
        var transaction = PaidTransaction(AddOnPlanId);

        await _sut.ApplyPaidTransactionAsync(transaction, Plan(AddOnPlanId), Now);

        var lot = Assert.Single(Lots);
        Assert.Equal((CreditSource.AddOn, 2, (DateTime?)Now.AddMonths(1)), (lot.Source, lot.Remaining, lot.ExpiresAt));
        Assert.Empty(Subscriptions);
    }

    [Fact]
    public async Task Apply_LegacyPlan_GrantsNonExpiringLegacyLot()
    {
        var transaction = PaidTransaction(1);

        await _sut.ApplyPaidTransactionAsync(transaction, Plan(1), Now);

        var lot = Assert.Single(Lots);
        Assert.Equal((CreditSource.Legacy, 50, (DateTime?)null), (lot.Source, lot.Remaining, lot.ExpiresAt));
        Assert.Empty(Subscriptions);
    }

    // ---------- Số lượt chốt trên giao dịch (sửa gói không đổi giao dịch đã tạo) ----------

    [Fact]
    public async Task Apply_UsesCreditsSnapshottedOnTheTransaction()
    {
        var transaction = PaidTransaction(CreativePlanId);
        transaction.AiCredits = 4;
        Plan(CreativePlanId).AiCredits = 10;

        await _sut.ApplyPaidTransactionAsync(transaction, Plan(CreativePlanId), Now);

        Assert.Equal(4, Assert.Single(Lots).Remaining);
    }

    [Fact]
    public async Task Apply_TransactionWithoutSnapshot_UsesPlanCredits()
    {
        var transaction = PaidTransaction(AddOnPlanId);
        Assert.Null(transaction.AiCredits);

        await _sut.ApplyPaidTransactionAsync(transaction, Plan(AddOnPlanId), Now);

        Assert.Equal(2, Assert.Single(Lots).Remaining);
    }

    [Fact]
    public async Task Advance_ScheduledCycle_UsesItsTransactionSnapshot()
    {
        var boundary = Now.AddHours(-1);
        SeedSubscription(CreativePlanId, SubscriptionStatus.Active, boundary.AddMonths(-1), boundary);
        var renewal = PaidTransaction(CreativePlanId, boundary.AddDays(-3));
        renewal.AiCredits = 4;
        _store.Seed(new UserSubscription
        {
            UserId = UserId, PlanId = CreativePlanId, Status = SubscriptionStatus.Scheduled,
            StartAt = boundary, EndAt = boundary.AddMonths(1), PaymentId = renewal.Id
        });
        Plan(CreativePlanId).AiCredits = 10;

        await _sut.AdvanceCyclesAsync(UserId, Now);

        Assert.Equal(4, Lots.Single(lot => lot.PaymentId == renewal.Id).Remaining);
    }

    // ---------- ApplyPaidTransactionAsync: idempotent, transaction ----------

    [Theory]
    [InlineData(CreativePlanId)]
    [InlineData(ReadingPlanId)]
    [InlineData(AddOnPlanId)]
    public async Task Apply_SameTransactionTwice_AppliesOnce(int planId)
    {
        var transaction = PaidTransaction(planId);
        await _sut.ApplyPaidTransactionAsync(transaction, Plan(planId), Now);
        var subscriptionCount = Subscriptions.Count;
        var lotCount = Lots.Count;

        await _sut.ApplyPaidTransactionAsync(transaction, Plan(planId), Now.AddMinutes(1));

        Assert.Equal((subscriptionCount, lotCount), (Subscriptions.Count, Lots.Count));
        Assert.DoesNotContain(Subscriptions, item => item.Status == SubscriptionStatus.Scheduled);
    }

    [Fact]
    public async Task Apply_RunsInsideTransactionWithAccountLock()
    {
        await _sut.ApplyPaidTransactionAsync(PaidTransaction(ReadingPlanId), Plan(ReadingPlanId), Now);

        Assert.Equal(["begin", $"lock:{UserId}", "commit"], _store.Calls);
    }

    [Fact]
    public async Task Apply_WhenGrantFails_RollsBackAndRethrows()
    {
        var quota = new Mock<ITokenQuotaService>();
        quota.Setup(service => service.GrantAsync(
                It.IsAny<int>(), It.IsAny<CreditSource>(), It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated grant failure"));
        var sut = CreateSut(quota.Object);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.ApplyPaidTransactionAsync(PaidTransaction(CreativePlanId), Plan(CreativePlanId), Now));

        Assert.Equal("simulated grant failure", error.Message);
        Assert.Equal("rollback", _store.Calls.Last());
        Assert.DoesNotContain("commit", _store.Calls);
    }

    // ---------- AdvanceCyclesAsync / GetActiveAsync ----------

    [Fact]
    public async Task Advance_ActivePastEnd_NoRenewal_Expires()
    {
        var expired = SeedSubscription(ReadingPlanId, SubscriptionStatus.Active, Now.AddMonths(-1), Now.AddMinutes(-1));

        await _sut.AdvanceCyclesAsync(UserId, Now);

        Assert.Equal(SubscriptionStatus.Expired, expired.Status);
        Assert.Null(await _sut.GetActiveAsync(UserId, Now));
    }

    [Fact]
    public async Task Advance_ScheduledDue_ActivatesAndGrantsCycleCredits()
    {
        var boundary = Now.AddHours(-1);
        var old = SeedSubscription(CreativePlanId, SubscriptionStatus.Active, boundary.AddMonths(-1), boundary);
        var oldCycle = SeedLot(CreditSource.Cycle, 1, boundary);
        var oldAddOn = SeedLot(CreditSource.AddOn, 1, boundary);
        var next = SeedSubscription(CreativePlanId, SubscriptionStatus.Scheduled, boundary, boundary.AddMonths(1));

        await _sut.AdvanceCyclesAsync(UserId, Now);

        Assert.Equal((SubscriptionStatus.Expired, SubscriptionStatus.Active), (old.Status, next.Status));
        var newLot = Lots.Single(lot => lot.PaymentId == next.PaymentId);
        Assert.Equal((CreditSource.Cycle, 4, (DateTime?)next.EndAt), (newLot.Source, newLot.Remaining, newLot.ExpiresAt));
        // Lượt chu kỳ và Add-on của chu kỳ cũ không chuyển sang: giữ nguyên hạn cũ (đã qua).
        Assert.Equal((boundary, boundary), (oldCycle.ExpiresAt!.Value, oldAddOn.ExpiresAt!.Value));
        var quota = await new TokenQuotaService(_store).GetMyQuotaAsync(UserId);
        Assert.Equal(4, quota.TotalRemaining);
    }

    [Fact]
    public async Task Advance_Twice_GrantsCycleCreditsOnce()
    {
        var boundary = Now.AddHours(-1);
        SeedSubscription(CreativePlanId, SubscriptionStatus.Active, boundary.AddMonths(-1), boundary);
        SeedSubscription(CreativePlanId, SubscriptionStatus.Scheduled, boundary, boundary.AddMonths(1));

        await _sut.AdvanceCyclesAsync(UserId, Now);
        await _sut.AdvanceCyclesAsync(UserId, Now.AddMinutes(15));

        Assert.Single(Lots);
    }

    [Fact]
    public async Task Advance_ScheduledReading_ActivatesWithoutCredits()
    {
        var boundary = Now.AddHours(-1);
        SeedSubscription(CreativePlanId, SubscriptionStatus.Active, boundary.AddMonths(-1), boundary);
        var next = SeedSubscription(ReadingPlanId, SubscriptionStatus.Scheduled, boundary, boundary.AddMonths(1));

        await _sut.AdvanceCyclesAsync(UserId, Now);

        Assert.Equal(SubscriptionStatus.Active, next.Status);
        Assert.Empty(Lots);
    }

    [Fact]
    public async Task Advance_WholeScheduledWindowAlreadyPassed_EndsExpired()
    {
        var firstEnd = Now.AddMonths(-3);
        var first = SeedSubscription(CreativePlanId, SubscriptionStatus.Active, firstEnd.AddMonths(-1), firstEnd);
        var missed = SeedSubscription(CreativePlanId, SubscriptionStatus.Scheduled, firstEnd, firstEnd.AddMonths(1));

        await _sut.AdvanceCyclesAsync(UserId, Now);

        Assert.Equal((SubscriptionStatus.Expired, SubscriptionStatus.Expired), (first.Status, missed.Status));
        Assert.Null(await _sut.GetActiveAsync(UserId, Now));
        var quota = await new TokenQuotaService(_store).GetMyQuotaAsync(UserId);
        Assert.Equal(0, quota.TotalRemaining);
    }

    [Fact]
    public async Task Advance_ActiveStillRunning_ChangesNothing()
    {
        var active = SeedSubscription(ReadingPlanId, SubscriptionStatus.Active, Now.AddDays(-3), Now.AddDays(27));
        var scheduled = SeedSubscription(ReadingPlanId, SubscriptionStatus.Scheduled, Now.AddDays(27), Now.AddDays(57));

        await _sut.AdvanceCyclesAsync(UserId, Now);

        Assert.Equal((SubscriptionStatus.Active, SubscriptionStatus.Scheduled), (active.Status, scheduled.Status));
    }

    [Fact]
    public async Task GetActiveAsync_ActiveRowPastEnd_ReturnsNull()
    {
        SeedSubscription(ReadingPlanId, SubscriptionStatus.Active, Now.AddMonths(-1), Now.AddSeconds(-1));

        Assert.Null(await _sut.GetActiveAsync(UserId, Now));
    }

    [Fact]
    public async Task GetActiveAsync_RunningCycle_ReturnsIt()
    {
        var active = SeedSubscription(CreativePlanId, SubscriptionStatus.Active, Now.AddDays(-1), Now.AddDays(29));

        Assert.Same(active, await _sut.GetActiveAsync(UserId, Now));
    }

    // ---------- SweepAsync ----------

    [Fact]
    public async Task Sweep_AdvancesDueAccountsInsideTransactionWithLock()
    {
        var expired = SeedSubscription(ReadingPlanId, SubscriptionStatus.Active, Now.AddMonths(-1), Now.AddMinutes(-1));

        var processed = await _sut.SweepAsync(Now);

        Assert.Equal(1, processed);
        Assert.Equal(SubscriptionStatus.Expired, expired.Status);
        Assert.Equal(["begin", $"lock:{UserId}", "commit"], _store.Calls);
    }

    [Fact]
    public async Task Sweep_ActiveEndingWithinThreeDays_SendsReminderOnce()
    {
        var endAt = Now.AddDays(3);
        var active = SeedSubscription(CreativePlanId, SubscriptionStatus.Active, endAt.AddMonths(-1), endAt);
        string? payload = null;
        _notifications
            .Setup(service => service.CreateAsync(
                UserId, NotificationType.SubscriptionExpiring, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback((int _, NotificationType _, string? json, CancellationToken _) => payload = json)
            .ReturnsAsync(new NotificationDto());

        await _sut.SweepAsync(Now);
        await _sut.SweepAsync(Now.AddMinutes(15));

        _notifications.Verify(
            service => service.CreateAsync(
                UserId, NotificationType.SubscriptionExpiring, It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Equal(Now, active.RenewalReminderSentAt);
        using var json = System.Text.Json.JsonDocument.Parse(payload!);
        Assert.Equal(active.Id, json.RootElement.GetProperty("subscriptionId").GetInt32());
        Assert.Equal("Gói Sáng tạo", json.RootElement.GetProperty("planName").GetString());
        Assert.Equal("2026-11-13T03:00:00Z", json.RootElement.GetProperty("endAt").GetString());
    }

    [Fact]
    public async Task Sweep_AlreadyRenewed_DoesNotRemind()
    {
        var endAt = Now.AddDays(2);
        SeedSubscription(CreativePlanId, SubscriptionStatus.Active, endAt.AddMonths(-1), endAt);
        SeedSubscription(CreativePlanId, SubscriptionStatus.Scheduled, endAt, endAt.AddMonths(1));

        await _sut.SweepAsync(Now);

        _notifications.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Sweep_EndingInFourDays_DoesNotRemind()
    {
        var endAt = Now.AddDays(4);
        SeedSubscription(ReadingPlanId, SubscriptionStatus.Active, endAt.AddMonths(-1), endAt);

        await _sut.SweepAsync(Now);

        _notifications.VerifyNoOtherCalls();
    }
}
