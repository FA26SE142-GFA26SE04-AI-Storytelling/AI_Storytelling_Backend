using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.TokenQuota.DTOs;
using StoryPlatform.Application.Features.TokenQuota.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.TokenQuota;

public class TokenQuotaServiceTests
{
    private const int OwnerId = 1;
    private const int ChildId = 10;

    private readonly InMemoryUnitOfWork _store = new();
    private readonly TokenQuotaService _sut;

    public TokenQuotaServiceTests()
    {
        _store.Seed(new UserAccount { Id = OwnerId, Role = UserRole.User, Status = AccountStatus.Active });
        _sut = new TokenQuotaService(_store);
    }

    private ChildProfile SeedChild(
        int id = ChildId, int ownerUserId = OwnerId, ChildProfileStatus status = ChildProfileStatus.Active) =>
        _store.Seed(new ChildProfile { Id = id, OwnerUserId = ownerUserId, Status = status });

    private CreditLot SeedLot(CreditSource source, int remaining, DateTime? expiresAt = null, int userId = OwnerId) =>
        _store.Seed(new CreditLot
        {
            UserId = userId,
            CreditType = CreditType.AI,
            Source = source,
            Total = Math.Max(remaining, 1),
            Remaining = remaining,
            ExpiresAt = expiresAt
        });

    private UserSubscription SeedSubscription(
        PlanType planType, string planName, SubscriptionStatus status, DateTime startAt, DateTime endAt) =>
        _store.Seed(new UserSubscription
        {
            UserId = OwnerId,
            PlanId = (int)planType + 1,
            Plan = new SubscriptionPlan { Id = (int)planType + 1, Name = planName, PlanType = planType },
            Status = status,
            StartAt = startAt,
            EndAt = endAt,
            PaymentId = _store.Items<UserSubscription>().Count + 1
        });

    // ---------- EnsureWithinQuotaAsync ----------

    [Fact]
    public async Task EnsureWithinQuotaAsync_NoLotsAndNoActiveChild_ThrowsConflict()
    {
        SeedChild(status: ChildProfileStatus.Draft);

        await Assert.ThrowsAsync<ConflictException>(() => _sut.EnsureWithinQuotaAsync(ChildId));

        Assert.Empty(_store.Items<CreditLot>());
    }

    [Fact]
    public async Task EnsureWithinQuotaAsync_FirstCallWithActiveChild_GrantsOneTrialCredit()
    {
        SeedChild();

        await _sut.EnsureWithinQuotaAsync(ChildId);

        var lot = Assert.Single(_store.Items<CreditLot>());
        Assert.Equal((OwnerId, CreditSource.Trial, 1, 1), (lot.UserId, lot.Source, lot.Total, lot.Remaining));
        Assert.Null(lot.ExpiresAt);
    }

    [Fact]
    public async Task EnsureWithinQuotaAsync_TrialAlreadyUsed_DoesNotGrantAgain()
    {
        SeedChild();
        SeedLot(CreditSource.Trial, remaining: 0);

        await Assert.ThrowsAsync<ConflictException>(() => _sut.EnsureWithinQuotaAsync(ChildId));

        Assert.Single(_store.Items<CreditLot>());
    }

    [Fact]
    public async Task EnsureWithinQuotaAsync_OnlyExpiredLots_ThrowsConflict()
    {
        SeedChild();
        SeedLot(CreditSource.Trial, remaining: 0);
        SeedLot(CreditSource.Cycle, remaining: 4, expiresAt: DateTime.UtcNow.AddDays(-1));

        var exception = await Assert.ThrowsAsync<ConflictException>(() => _sut.EnsureWithinQuotaAsync(ChildId));

        Assert.Contains("Add-on", exception.Message);
    }

    [Fact]
    public async Task EnsureWithinQuotaAsync_NonParentOwner_GetsNoTrialCredit()
    {
        _store.Seed(new UserAccount { Id = 2, Role = UserRole.ContentCreator, Status = AccountStatus.Active });
        SeedChild(id: 20, ownerUserId: 2);

        await Assert.ThrowsAsync<ConflictException>(() => _sut.EnsureWithinQuotaAsync(20));

        Assert.Empty(_store.Items<CreditLot>());
    }

    [Fact]
    public async Task EnsureWithinQuotaAsync_ChildNotFound_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.EnsureWithinQuotaAsync(999));
    }

    // ---------- IncrementUsageAsync ----------

    [Fact]
    public async Task IncrementUsageAsync_ConsumesLotExpiringSoonestFirst()
    {
        SeedChild();
        var trial = SeedLot(CreditSource.Trial, remaining: 1);
        var cycle = SeedLot(CreditSource.Cycle, remaining: 4, expiresAt: DateTime.UtcNow.AddDays(10));
        var addOn = SeedLot(CreditSource.AddOn, remaining: 2, expiresAt: DateTime.UtcNow.AddDays(3));

        await _sut.IncrementUsageAsync(ChildId);

        Assert.Equal((1, 4, 1), (trial.Remaining, cycle.Remaining, addOn.Remaining));
    }

    [Fact]
    public async Task IncrementUsageAsync_NonExpiringLotsAreUsedLast()
    {
        SeedChild();
        var legacy = SeedLot(CreditSource.Legacy, remaining: 5);
        var cycle = SeedLot(CreditSource.Cycle, remaining: 1, expiresAt: DateTime.UtcNow.AddDays(20));

        await _sut.IncrementUsageAsync(ChildId);
        await _sut.IncrementUsageAsync(ChildId);

        Assert.Equal((4, 0), (legacy.Remaining, cycle.Remaining));
    }

    [Fact]
    public async Task IncrementUsageAsync_NoUsableLot_ThrowsConflict()
    {
        SeedChild();
        var spent = SeedLot(CreditSource.Trial, remaining: 0);
        var expired = SeedLot(CreditSource.AddOn, remaining: 2, expiresAt: DateTime.UtcNow.AddMinutes(-1));

        await Assert.ThrowsAsync<ConflictException>(() => _sut.IncrementUsageAsync(ChildId));

        Assert.Equal((0, 2), (spent.Remaining, expired.Remaining));
    }

    [Fact]
    public async Task IncrementUsageAsync_AcquiresAccountLock()
    {
        SeedChild();
        SeedLot(CreditSource.Legacy, remaining: 1);

        await _sut.IncrementUsageAsync(ChildId);

        Assert.Equal([$"lock:{OwnerId}"], _store.Calls);
    }

    [Fact]
    public async Task TwoRequestsWithOneCreditLeft_SecondIsRejected()
    {
        SeedChild();
        SeedLot(CreditSource.Legacy, remaining: 1);

        await _sut.IncrementUsageAsync(ChildId);

        await Assert.ThrowsAsync<ConflictException>(() => _sut.IncrementUsageAsync(ChildId));
    }

    [Fact]
    public async Task TwoChildrenOfSameOwner_ShareOneBalance()
    {
        SeedChild();
        SeedChild(id: 11);
        SeedLot(CreditSource.Trial, remaining: 1);

        await _sut.EnsureWithinQuotaAsync(ChildId);
        await _sut.IncrementUsageAsync(ChildId);

        await Assert.ThrowsAsync<ConflictException>(() => _sut.EnsureWithinQuotaAsync(11));
    }

    // ---------- GrantAsync ----------

    [Fact]
    public async Task GrantAsync_CreatesLotWithGivenSourceAndExpiry()
    {
        var expiresAt = DateTime.UtcNow.AddMonths(1);

        var granted = await _sut.GrantAsync(OwnerId, CreditSource.Cycle, 4, expiresAt, paymentId: 7);

        Assert.True(granted);
        var lot = Assert.Single(_store.Items<CreditLot>());
        Assert.Equal(
            (OwnerId, CreditType.AI, CreditSource.Cycle, 4, 4, (DateTime?)expiresAt, (int?)7),
            (lot.UserId, lot.CreditType, lot.Source, lot.Total, lot.Remaining, lot.ExpiresAt, lot.PaymentId));
    }

    [Fact]
    public async Task GrantAsync_SamePaymentIdTwice_GrantsOnce()
    {
        var first = await _sut.GrantAsync(OwnerId, CreditSource.AddOn, 2, DateTime.UtcNow.AddDays(5), paymentId: 7);
        var second = await _sut.GrantAsync(OwnerId, CreditSource.AddOn, 2, DateTime.UtcNow.AddDays(5), paymentId: 7);

        Assert.True(first);
        Assert.False(second);
        Assert.Single(_store.Items<CreditLot>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task GrantAsync_NonPositiveAmount_GrantsNothing(int amount)
    {
        var granted = await _sut.GrantAsync(OwnerId, CreditSource.Cycle, amount, null, paymentId: 7);

        Assert.False(granted);
        Assert.Empty(_store.Items<CreditLot>());
    }

    // ---------- GetMyQuotaAsync ----------

    [Fact]
    public async Task GetMyQuotaAsync_SplitsCreditsBySource()
    {
        var now = DateTime.UtcNow;
        SeedLot(CreditSource.Trial, remaining: 1);
        SeedLot(CreditSource.Cycle, remaining: 3, expiresAt: now.AddDays(12));
        SeedLot(CreditSource.AddOn, remaining: 2, expiresAt: now.AddDays(12));
        SeedLot(CreditSource.Legacy, remaining: 5);
        SeedLot(CreditSource.Cycle, remaining: 4, expiresAt: now.AddDays(-2));
        SeedSubscription(PlanType.Creative, "Gói Sáng tạo", SubscriptionStatus.Active, now.AddDays(-18), now.AddDays(12));
        SeedSubscription(PlanType.Reading, "Gói Đọc", SubscriptionStatus.Scheduled, now.AddDays(12), now.AddDays(42));

        var result = await _sut.GetMyQuotaAsync(OwnerId);

        Assert.Equal(
            (1, 3, 2, 5, 11),
            (result.TrialCredits, result.CycleCredits, result.AddOnCredits, result.OtherCredits, result.TotalRemaining));
        Assert.Equal(("Creative", "Gói Sáng tạo"), (result.Plan!.PlanType, result.Plan.PlanName));
        Assert.Equal(now.AddDays(12), result.Plan.EndAt);
        Assert.Equal(("Reading", "Gói Đọc"), (result.NextPlan!.PlanType, result.NextPlan.PlanName));
    }

    [Fact]
    public async Task GetMyQuotaAsync_FreeAccount_PlanIsNull()
    {
        var result = await _sut.GetMyQuotaAsync(OwnerId);

        Assert.Null(result.Plan);
        Assert.Null(result.NextPlan);
        Assert.Equal(0, result.TotalRemaining);
    }

    [Fact]
    public async Task GetMyQuotaAsync_ActiveRowPastItsEnd_IsNotTheCurrentPlan()
    {
        var now = DateTime.UtcNow;
        SeedSubscription(PlanType.Reading, "Gói Đọc", SubscriptionStatus.Active, now.AddMonths(-1), now.AddMinutes(-1));

        var result = await _sut.GetMyQuotaAsync(OwnerId);

        Assert.Null(result.Plan);
    }

    [Fact]
    public async Task GetMyQuotaAsync_ParentWithActiveChild_SeesTrialCredit()
    {
        SeedChild();

        var result = await _sut.GetMyQuotaAsync(OwnerId);

        Assert.Equal((1, 1), (result.TrialCredits, result.TotalRemaining));
    }

    // ---------- GetStatusForChildAsync ----------

    [Fact]
    public async Task GetStatusForChildAsync_Owner_ReturnsAccountBalance()
    {
        var now = DateTime.UtcNow;
        SeedChild();
        SeedLot(CreditSource.Trial, remaining: 0);
        SeedLot(CreditSource.Cycle, remaining: 4, expiresAt: now.AddDays(9));
        SeedLot(CreditSource.AddOn, remaining: 2, expiresAt: now.AddDays(9));
        SeedSubscription(PlanType.Creative, "Gói Sáng tạo", SubscriptionStatus.Active, now.AddDays(-21), now.AddDays(9));

        var result = await _sut.GetStatusForChildAsync(OwnerId, ChildId);

        Assert.False(result.IsUnlimited);
        Assert.Equal((6, 6, 0), (result.Remaining, result.QuotaLimit, result.QuotaUsed));
        Assert.Equal(DateOnly.FromDateTime(now.AddDays(9)), result.PeriodEnd);
    }

    [Fact]
    public async Task GetStatusForChildAsync_NonOwnerNonAdmin_ThrowsForbidden()
    {
        SeedChild();
        _store.Seed(new UserAccount { Id = 2, Role = UserRole.User, Status = AccountStatus.Active });

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.GetStatusForChildAsync(2, ChildId));
    }

    [Fact]
    public async Task GetStatusForChildAsync_Administrator_Allowed()
    {
        SeedChild();
        SeedLot(CreditSource.Legacy, remaining: 3);
        _store.Seed(new UserAccount { Id = 2, Role = UserRole.Administrator, Status = AccountStatus.Active });

        var result = await _sut.GetStatusForChildAsync(2, ChildId);

        Assert.Equal(4, result.Remaining);
    }

    // ---------- Admin: AdjustAsync (UC-L2-08) ----------

    private const int AdminId = 90;

    private void SeedAdmin() =>
        _store.Seed(new UserAccount { Id = AdminId, Role = UserRole.Administrator, Status = AccountStatus.Active, FullName = "Quản trị" });

    private static AdjustCreditsRequestDto Adjust(int amount, string? reason = "Bù lỗi sinh truyện", int userId = OwnerId) =>
        new() { UserId = userId, Amount = amount, Reason = reason! };

    [Fact]
    public async Task AdjustAsync_Grant_CreatesNonExpiringAdminLot_AndRecordsAdjustment()
    {
        SeedAdmin();
        _store.Seed(new UserAccount { Id = 2, Role = UserRole.User, Status = AccountStatus.Active });
        SeedLot(CreditSource.Legacy, remaining: 5, userId: 2);

        var result = await _sut.AdjustAsync(AdminId, Adjust(3));

        var lot = Assert.Single(_store.Items<CreditLot>(), item => item.UserId == OwnerId);
        Assert.Equal((CreditSource.AdminAdjust, 3, 3, (DateTime?)null), (lot.Source, lot.Total, lot.Remaining, lot.ExpiresAt));
        var adjustment = Assert.Single(_store.Items<CreditAdjustment>());
        Assert.Equal((OwnerId, 3, "Bù lỗi sinh truyện", AdminId), (adjustment.UserId, adjustment.Amount, adjustment.Reason, adjustment.AdminId));
        Assert.Equal(
            (adjustment.Id, 3, (int?)3, "Quản trị"),
            (result.Id, result.Amount, result.TotalRemainingAfter, result.AdminName));
        Assert.Equal(5, _store.Items<CreditLot>().Single(item => item.UserId == 2).Remaining);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task AdjustAsync_MissingReason_ThrowsBadRequest(string? reason)
    {
        SeedAdmin();

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.AdjustAsync(AdminId, Adjust(3, reason)));

        Assert.Empty(_store.Items<CreditLot>());
        Assert.Empty(_store.Items<CreditAdjustment>());
    }

    [Fact]
    public async Task AdjustAsync_ReasonLongerThan500_ThrowsBadRequest()
    {
        SeedAdmin();

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.AdjustAsync(AdminId, Adjust(3, new string('a', 501))));
    }

    [Fact]
    public async Task AdjustAsync_ZeroAmount_ThrowsBadRequest()
    {
        SeedAdmin();

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.AdjustAsync(AdminId, Adjust(0)));
    }

    [Fact]
    public async Task AdjustAsync_UserNotFound_ThrowsNotFound()
    {
        SeedAdmin();

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.AdjustAsync(AdminId, Adjust(3, userId: 999)));
    }

    [Theory]
    [InlineData(UserRole.ContentCreator)]
    [InlineData(UserRole.Administrator)]
    [InlineData(UserRole.Moderator)]
    public async Task AdjustAsync_NonParentAccount_ThrowsBadRequest(UserRole role)
    {
        SeedAdmin();
        _store.Seed(new UserAccount { Id = 2, Role = role, Status = AccountStatus.Active });

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.AdjustAsync(AdminId, Adjust(3, userId: 2)));

        Assert.Empty(_store.Items<CreditLot>());
    }

    [Fact]
    public async Task AdjustAsync_Deduct_TakesFromSoonestExpiringLotsFirst()
    {
        SeedAdmin();
        var legacy = SeedLot(CreditSource.Legacy, remaining: 5);
        var cycle = SeedLot(CreditSource.Cycle, remaining: 4, expiresAt: DateTime.UtcNow.AddDays(10));
        var addOn = SeedLot(CreditSource.AddOn, remaining: 2, expiresAt: DateTime.UtcNow.AddDays(3));

        var result = await _sut.AdjustAsync(AdminId, Adjust(-3, "Thu hồi lượt cấp nhầm"));

        Assert.Equal((0, 3, 5), (addOn.Remaining, cycle.Remaining, legacy.Remaining));
        Assert.Equal(8, result.TotalRemainingAfter);
        Assert.Equal(-3, Assert.Single(_store.Items<CreditAdjustment>()).Amount);
        Assert.Equal(3, _store.Items<CreditLot>().Count);
    }

    [Fact]
    public async Task AdjustAsync_DeductMoreThanRemaining_ThrowsBadRequestAndChangesNothing()
    {
        SeedAdmin();
        var lot = SeedLot(CreditSource.Legacy, remaining: 2);

        var error = await Assert.ThrowsAsync<BadRequestException>(() => _sut.AdjustAsync(AdminId, Adjust(-3)));

        Assert.Contains("chỉ còn 2 lượt", error.Message);
        Assert.Equal(2, lot.Remaining);
        Assert.Empty(_store.Items<CreditAdjustment>());
        Assert.Equal("rollback", _store.Calls.Last());
    }

    [Fact]
    public async Task AdjustAsync_DeductIgnoresExpiredLots()
    {
        SeedAdmin();
        var expired = SeedLot(CreditSource.Cycle, remaining: 4, expiresAt: DateTime.UtcNow.AddDays(-1));
        var live = SeedLot(CreditSource.Legacy, remaining: 1);

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.AdjustAsync(AdminId, Adjust(-2)));

        Assert.Equal((4, 1), (expired.Remaining, live.Remaining));
    }

    [Fact]
    public async Task AdjustAsync_DeductExactlyAllRemaining_LeavesZero()
    {
        SeedAdmin();
        SeedLot(CreditSource.Legacy, remaining: 2);

        var result = await _sut.AdjustAsync(AdminId, Adjust(-2));

        Assert.Equal(0, result.TotalRemainingAfter);
    }

    [Fact]
    public async Task AdjustAsync_RunsInTransactionWithAccountLock()
    {
        SeedAdmin();

        await _sut.AdjustAsync(AdminId, Adjust(1));

        Assert.Equal(["begin", $"lock:{OwnerId}", "commit"], _store.Calls);
    }

    [Fact]
    public async Task AdjustAsync_WritesAuditWithBeforeAndAfterTotals()
    {
        SeedAdmin();
        SeedLot(CreditSource.Legacy, remaining: 2);
        var audit = new Mock<IAuditLogWriter>();
        object? before = null, after = null;
        audit.Setup(writer => writer.LogAsync(
                AdminId, "CreditAdjusted", nameof(CreditAdjustment), It.IsAny<int>(),
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Callback((int? _, string _, string _, int _, object? b, object? a, CancellationToken _) => (before, after) = (b, a));
        var sut = new TokenQuotaService(_store, audit.Object);

        await sut.AdjustAsync(AdminId, Adjust(3));

        Assert.Contains("\"totalRemaining\":2", System.Text.Json.JsonSerializer.Serialize(before));
        var afterJson = System.Text.Json.JsonSerializer.Serialize(after);
        Assert.Contains("\"totalRemaining\":5", afterJson);
        Assert.Contains("\"amount\":3", afterJson);
    }

    [Fact]
    public async Task GetMyQuotaAsync_AdminLotsCountAsOtherCredits()
    {
        SeedAdmin();
        SeedLot(CreditSource.Legacy, remaining: 2);
        await _sut.AdjustAsync(AdminId, Adjust(3));

        var result = await _sut.GetMyQuotaAsync(OwnerId);

        Assert.Equal((5, 5), (result.OtherCredits, result.TotalRemaining));
    }

    [Fact]
    public async Task AdminGrantedCredit_CanBeUsedToCreateAStory()
    {
        SeedAdmin();
        SeedChild();
        SeedLot(CreditSource.Trial, remaining: 0);
        await _sut.AdjustAsync(AdminId, Adjust(1));

        await _sut.EnsureWithinQuotaAsync(ChildId);
        await _sut.IncrementUsageAsync(ChildId);

        await Assert.ThrowsAsync<ConflictException>(() => _sut.EnsureWithinQuotaAsync(ChildId));
    }

    // ---------- Admin: ListAdjustmentsAsync / GetQuotaForUserAsync ----------

    [Fact]
    public async Task ListAdjustmentsAsync_FiltersByUserNewestFirst()
    {
        SeedAdmin();
        _store.Seed(new UserAccount { Id = 2, Role = UserRole.User, Status = AccountStatus.Active });
        var now = DateTime.UtcNow;
        _store.Seed(new CreditAdjustment { UserId = OwnerId, Amount = 2, Reason = "cũ", AdminId = AdminId, CreatedAt = now.AddDays(-2) });
        _store.Seed(new CreditAdjustment { UserId = 2, Amount = 9, Reason = "người khác", AdminId = AdminId, CreatedAt = now.AddDays(-1) });
        _store.Seed(new CreditAdjustment { UserId = OwnerId, Amount = -1, Reason = "mới", AdminId = AdminId, CreatedAt = now });

        var mine = await _sut.ListAdjustmentsAsync(OwnerId, pageIndex: 1, pageSize: 20);
        var all = await _sut.ListAdjustmentsAsync(null, pageIndex: 1, pageSize: 20);

        Assert.Equal(["mới", "cũ"], mine.Items.Select(item => item.Reason));
        Assert.Equal("Quản trị", mine.Items[0].AdminName);
        Assert.Null(mine.Items[0].TotalRemainingAfter);
        Assert.Equal(3, all.TotalCount);
    }

    [Fact]
    public async Task GetQuotaForUserAsync_ReturnsBalanceWithoutGrantingTrial()
    {
        SeedChild();

        var result = await _sut.GetQuotaForUserAsync(OwnerId);

        Assert.Equal(0, result.TotalRemaining);
        Assert.Empty(_store.Items<CreditLot>());
    }

    [Fact]
    public async Task GetQuotaForUserAsync_UserNotFound_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetQuotaForUserAsync(999));
    }
    // ---------- Lịch sử trừ lượt (credit_usages) ----------

    [Fact]
    public async Task IncrementUsageAsync_RecordsOneUsageAgainstTheConsumedLot()
    {
        SeedChild();
        SeedLot(CreditSource.Legacy, remaining: 5);
        var addOn = SeedLot(CreditSource.AddOn, remaining: 2, expiresAt: DateTime.UtcNow.AddDays(3));

        await _sut.IncrementUsageAsync(ChildId);

        var usage = Assert.Single(_store.Items<CreditUsage>());
        Assert.Equal(
            (OwnerId, addOn.Id, 1, CreditUsageReason.StoryRequest),
            (usage.UserId, usage.LotId, usage.Amount, usage.Reason));
    }

    [Fact]
    public async Task IncrementUsageAsync_NoUsableLot_RecordsNothing()
    {
        SeedChild();
        SeedLot(CreditSource.Trial, remaining: 0);

        await Assert.ThrowsAsync<ConflictException>(() => _sut.IncrementUsageAsync(ChildId));

        Assert.Empty(_store.Items<CreditUsage>());
    }

    [Fact]
    public async Task AdjustAsync_Deduct_RecordsUsagePerLotTouched()
    {
        SeedAdmin();
        var legacy = SeedLot(CreditSource.Legacy, remaining: 5);
        var addOn = SeedLot(CreditSource.AddOn, remaining: 2, expiresAt: DateTime.UtcNow.AddDays(3));

        await _sut.AdjustAsync(AdminId, Adjust(-3, "Thu hồi lượt cấp nhầm"));

        Assert.Equal(
            [(addOn.Id, 2), (legacy.Id, 1)],
            _store.Items<CreditUsage>().Select(usage => (usage.LotId, usage.Amount)));
        Assert.All(_store.Items<CreditUsage>(), usage => Assert.Equal(CreditUsageReason.AdminDeduction, usage.Reason));
    }

    [Fact]
    public async Task AdjustAsync_Grant_RecordsNoUsage()
    {
        SeedAdmin();

        await _sut.AdjustAsync(AdminId, Adjust(3));

        Assert.Empty(_store.Items<CreditUsage>());
    }

    [Fact]
    public async Task AdjustAsync_RejectedDeduction_RecordsNoUsage()
    {
        SeedAdmin();
        SeedLot(CreditSource.Legacy, remaining: 1);

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.AdjustAsync(AdminId, Adjust(-2)));

        Assert.Empty(_store.Items<CreditUsage>());
    }
}
