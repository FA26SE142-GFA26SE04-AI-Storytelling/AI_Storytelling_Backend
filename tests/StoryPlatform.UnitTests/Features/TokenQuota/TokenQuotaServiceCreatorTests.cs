using StoryPlatform.Application.Features.PlanBenefits.Services;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.TokenQuota.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.TokenQuota;

/// <summary>Lượt của Content Creator (Luồng 2, 2f): hạn mức tháng, Gói Creator, lượt AI và lượt gửi duyệt.</summary>
public class TokenQuotaServiceCreatorTests
{
    private const int CreatorId = 50;

    private readonly InMemoryUnitOfWork _store = new();
    private readonly TokenQuotaService _sut;

    public TokenQuotaServiceCreatorTests()
    {
        _store.Seed(new UserAccount { Id = CreatorId, Role = UserRole.ContentCreator, Status = AccountStatus.Active });
        _sut = new TokenQuotaService(_store);
    }

    /// <summary>Hạn mức tháng hết hạn lúc 00:00 ngày 1 tháng sau theo giờ Việt Nam (tính ra UTC).</summary>
    private static DateTime CurrentMonthEndUtc()
    {
        var vietnamNow = DateTime.UtcNow.AddHours(7);
        var nextMonth = new DateTime(vietnamNow.Year, vietnamNow.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
        return nextMonth.AddHours(-7);
    }

    private CreditLot Lot(CreditType type, CreditSource source, int remaining, DateTime? expiresAt = null) => _store.Seed(new CreditLot
    {
        UserId = CreatorId, CreditType = type, Source = source, Total = Math.Max(remaining, 1), Remaining = remaining,
        ExpiresAt = expiresAt
    });

    private IEnumerable<CreditLot> Lots(CreditType type, CreditSource source) =>
        _store.Items<CreditLot>().Where(lot => lot.CreditType == type && lot.Source == source);

    private ModerationItem Item(int id, int? previousItemId = null, ModerationItemStatus status = ModerationItemStatus.Pending) =>
        _store.Seed(new ModerationItem
        {
            Id = id, ItemType = ModerationItemType.PublicStoryReview, Status = status, CreatedByUserId = CreatorId,
            PreviousItemId = previousItemId, IsResubmission = previousItemId != null
        });

    // ---------- Hạn mức tháng ----------

    [Fact]
    public async Task GetCreatorQuotaAsync_GrantsTheMonthlyAllowanceOnce_ExpiringAtTheVietnamMonthEnd()
    {
        var first = await _sut.GetCreatorQuotaAsync(CreatorId);
        await _sut.GetCreatorQuotaAsync(CreatorId);

        var ai = Assert.Single(Lots(CreditType.AI, CreditSource.CreatorMonthly));
        var submit = Assert.Single(Lots(CreditType.Submit, CreditSource.CreatorMonthly));
        Assert.Equal((2, 5), (ai.Remaining, submit.Remaining));
        Assert.Equal((CurrentMonthEndUtc(), CurrentMonthEndUtc()), (ai.ExpiresAt!.Value, submit.ExpiresAt!.Value));
        Assert.Equal(
            (2, 2, 0, 2, 5, 5, 0, 5, CurrentMonthEndUtc()),
            (first.AiCredits.MonthlyRemaining, first.AiCredits.MonthlyAllowance, first.AiCredits.PurchasedRemaining, first.AiCredits.TotalRemaining,
                first.SubmitCredits.MonthlyRemaining, first.SubmitCredits.MonthlyAllowance, first.SubmitCredits.PurchasedRemaining, first.SubmitCredits.TotalRemaining,
                first.MonthlyAllowanceResetsAt));
    }

    [Fact]
    public async Task MonthlyAllowance_IsNotCarriedOver_ANewMonthGetsAFreshLot()
    {
        var lastMonthEnd = CurrentMonthEndUtc().AddMonths(-1);
        Lot(CreditType.AI, CreditSource.CreatorMonthly, 1, lastMonthEnd);
        Lot(CreditType.Submit, CreditSource.CreatorMonthly, 3, lastMonthEnd);

        var quota = await _sut.GetCreatorQuotaAsync(CreatorId);

        Assert.Equal((2, 5), (quota.AiCredits.TotalRemaining, quota.SubmitCredits.TotalRemaining));
        Assert.Equal(2, Lots(CreditType.AI, CreditSource.CreatorMonthly).Count());
    }

    [Theory]
    [InlineData(UserRole.ContentCreator, AccountStatus.Suspended)]
    [InlineData(UserRole.ContentCreator, AccountStatus.Registered)]
    [InlineData(UserRole.User, AccountStatus.Active)]
    public async Task MonthlyAllowance_IsOnlyForActiveCreators(UserRole role, AccountStatus status)
    {
        _store.Seed(new UserAccount { Id = 60, Role = role, Status = status });

        var quota = await _sut.GetCreatorQuotaAsync(60);

        Assert.Equal((0, 0), (quota.AiCredits.TotalRemaining, quota.SubmitCredits.TotalRemaining));
        Assert.Empty(_store.Items<CreditLot>());
    }

    [Fact]
    public async Task GetCreatorQuotaAsync_SplitsMonthlyFromPurchasedCredits()
    {
        Lot(CreditType.AI, CreditSource.CreatorPack, 6);
        Lot(CreditType.Submit, CreditSource.CreatorPack, 20);
        Lot(CreditType.AI, CreditSource.CreatorPack, 3);

        var quota = await _sut.GetCreatorQuotaAsync(CreatorId);

        Assert.Equal((2, 9, 11), (quota.AiCredits.MonthlyRemaining, quota.AiCredits.PurchasedRemaining, quota.AiCredits.TotalRemaining));
        Assert.Equal((5, 20, 25), (quota.SubmitCredits.MonthlyRemaining, quota.SubmitCredits.PurchasedRemaining, quota.SubmitCredits.TotalRemaining));
    }

    // ---------- Lượt AI của Creator ----------

    [Fact]
    public async Task CreatorAiCredits_MonthlyAllowanceIsUsedBeforePurchasedCredits()
    {
        var pack = Lot(CreditType.AI, CreditSource.CreatorPack, 6);

        await _sut.EnsureCreatorWithinQuotaAsync(CreatorId);
        await _sut.IncrementCreatorUsageAsync(CreatorId);
        await _sut.IncrementCreatorUsageAsync(CreatorId);
        await _sut.IncrementCreatorUsageAsync(CreatorId);

        var monthly = Assert.Single(Lots(CreditType.AI, CreditSource.CreatorMonthly));
        Assert.Equal((0, 5), (monthly.Remaining, pack.Remaining));
        Assert.Equal(3, _store.Items<CreditUsage>().Count(usage => usage.Reason == CreditUsageReason.StoryRequest));
        Assert.Equal([$"lock:{CreatorId}", $"lock:{CreatorId}", $"lock:{CreatorId}"], _store.Calls);
    }

    [Fact]
    public async Task CreatorAiCredits_OutOfCredits_IsRefusedWith409()
    {
        await _sut.IncrementCreatorUsageAsync(CreatorId);
        await _sut.IncrementCreatorUsageAsync(CreatorId);

        var ensure = await Assert.ThrowsAsync<ConflictException>(() => _sut.EnsureCreatorWithinQuotaAsync(CreatorId));
        await Assert.ThrowsAsync<ConflictException>(() => _sut.IncrementCreatorUsageAsync(CreatorId));

        Assert.Contains("Gói Creator", ensure.Message);
    }

    [Fact]
    public async Task CreatorAiCredits_DoNotSpendSubmissionCredits()
    {
        await _sut.IncrementCreatorUsageAsync(CreatorId);

        Assert.Equal(5, Assert.Single(Lots(CreditType.Submit, CreditSource.CreatorMonthly)).Remaining);
    }

    // ---------- Lượt gửi duyệt ----------

    [Fact]
    public async Task ChargeSubmissionAsync_TakesOneSubmitCredit_MonthlyFirst_AndRecordsTheItem()
    {
        var pack = Lot(CreditType.Submit, CreditSource.CreatorPack, 20);
        Item(1);

        await _sut.ChargeSubmissionAsync(CreatorId, 1, changesRequestedItemId: null);

        var monthly = Assert.Single(Lots(CreditType.Submit, CreditSource.CreatorMonthly));
        Assert.Equal((4, 20), (monthly.Remaining, pack.Remaining));
        var usage = Assert.Single(_store.Items<CreditUsage>());
        Assert.Equal(
            (CreditUsageReason.PublicSubmission, (int?)1, monthly.Id, (DateTime?)null),
            (usage.Reason, usage.ModerationItemId, usage.LotId, usage.RefundedAt));
        Assert.Equal(2, Assert.Single(Lots(CreditType.AI, CreditSource.CreatorMonthly)).Remaining);
    }

    [Fact]
    public async Task ChargeSubmissionAsync_OutOfSubmitCredits_IsRefusedWith409_AndChangesNothing()
    {
        Lot(CreditType.Submit, CreditSource.CreatorMonthly, 0, CurrentMonthEndUtc());
        Lot(CreditType.AI, CreditSource.CreatorMonthly, 2, CurrentMonthEndUtc());
        Item(1);

        var error = await Assert.ThrowsAsync<ConflictException>(() => _sut.ChargeSubmissionAsync(CreatorId, 1, null));

        Assert.Contains("Gói Creator", error.Message);
        Assert.Empty(_store.Items<CreditUsage>());
    }

    [Fact]
    public async Task RefundSubmissionAsync_RestoresTheCredit_Once()
    {
        Item(1);
        await _sut.ChargeSubmissionAsync(CreatorId, 1, null);

        await _sut.RefundSubmissionAsync(1);
        await _sut.RefundSubmissionAsync(1);

        Assert.Equal(5, Assert.Single(Lots(CreditType.Submit, CreditSource.CreatorMonthly)).Remaining);
        Assert.NotNull(Assert.Single(_store.Items<CreditUsage>()).RefundedAt);
    }

    [Fact]
    public async Task RefundSubmissionAsync_ItemThatNeverPaid_IsANoOp()
    {
        Item(1);

        await _sut.RefundSubmissionAsync(1);

        Assert.Empty(_store.Items<CreditUsage>());
        Assert.Empty(_store.Items<CreditLot>());
    }

    [Fact]
    public async Task ResubmissionAfterChangesRequested_IsFree_AndCarriesTheOriginalCredit()
    {
        Item(1, status: ModerationItemStatus.ChangesRequested);
        await _sut.ChargeSubmissionAsync(CreatorId, 1, null);
        Item(2, previousItemId: 1);

        await _sut.ChargeSubmissionAsync(CreatorId, 2, changesRequestedItemId: 1);

        Assert.Equal(4, Assert.Single(Lots(CreditType.Submit, CreditSource.CreatorMonthly)).Remaining);
        Assert.Equal((int?)2, Assert.Single(_store.Items<CreditUsage>()).ModerationItemId);
    }

    [Fact]
    public async Task ResubmissionAfterChangesRequested_ThenRejected_RefundsTheOriginalCredit()
    {
        Item(1, status: ModerationItemStatus.ChangesRequested);
        await _sut.ChargeSubmissionAsync(CreatorId, 1, null);
        Item(2, previousItemId: 1);
        await _sut.ChargeSubmissionAsync(CreatorId, 2, changesRequestedItemId: 1);

        await _sut.RefundSubmissionAsync(2);

        Assert.Equal(5, Assert.Single(Lots(CreditType.Submit, CreditSource.CreatorMonthly)).Remaining);
    }

    [Fact]
    public async Task ResubmissionAfterChangesRequested_WithdrawnAndSubmittedAgain_IsCharged()
    {
        // Gửi (trả 1 lượt) → bị yêu cầu sửa → gửi lại (miễn phí) → rút (hoàn lượt) → gửi lại lần nữa: lượt đã hoàn nên phải trả lại.
        Item(1, status: ModerationItemStatus.ChangesRequested);
        await _sut.ChargeSubmissionAsync(CreatorId, 1, null);
        Item(2, previousItemId: 1, status: ModerationItemStatus.Withdrawn);
        await _sut.ChargeSubmissionAsync(CreatorId, 2, changesRequestedItemId: 1);
        await _sut.RefundSubmissionAsync(2);
        Item(3, previousItemId: 1);

        await _sut.ChargeSubmissionAsync(CreatorId, 3, changesRequestedItemId: 1);

        Assert.Equal(4, Assert.Single(Lots(CreditType.Submit, CreditSource.CreatorMonthly)).Remaining);
        Assert.Equal(1, _store.Items<CreditUsage>().Count(usage => usage.ModerationItemId == 3 && usage.RefundedAt == null));
    }

    [Fact]
    public async Task ResubmissionAfterChangesRequested_OnASubmissionFromBeforeCreditsExisted_IsFree()
    {
        Item(1, status: ModerationItemStatus.ChangesRequested);
        Item(2, previousItemId: 1);

        await _sut.ChargeSubmissionAsync(CreatorId, 2, changesRequestedItemId: 1);

        Assert.Empty(_store.Items<CreditUsage>());
    }

    [Fact]
    public async Task ChargeSubmissionAsync_RunsUnderTheAccountLock()
    {
        Item(1);

        await _sut.ChargeSubmissionAsync(CreatorId, 1, null);

        Assert.Equal([$"lock:{CreatorId}"], _store.Calls);
    }

    [Fact]
    public async Task GetCreatorQuotaAsync_UsesConfiguredMonthlyAllowance()
    {
        _store.Seed(new PlanSettings { Id = PlanSettings.SingletonId, CreatorMonthlyAiCredits = 3, CreatorMonthlySubmitCredits = 7 });
        var sut = new TokenQuotaService(_store, planSettings: new PlanSettingsProvider(_store));

        var quota = await sut.GetCreatorQuotaAsync(CreatorId);

        Assert.Equal((3, 7), (Assert.Single(Lots(CreditType.AI, CreditSource.CreatorMonthly)).Total,
            Assert.Single(Lots(CreditType.Submit, CreditSource.CreatorMonthly)).Total));
        Assert.Equal((3, 7), (quota.AiCredits.MonthlyAllowance, quota.SubmitCredits.MonthlyAllowance));
    }
}
