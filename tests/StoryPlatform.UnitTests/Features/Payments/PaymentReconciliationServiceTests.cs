using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Application.Features.Payments.Interfaces;
using StoryPlatform.Application.Features.Payments.Services;
using StoryPlatform.Application.Features.TokenQuota.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.UnitTests.Features.TokenQuota;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Payments;

public class PaymentReconciliationServiceTests
{
    private const int ParentId = 1;
    private const int AdminId = 9;
    private const int ReadingPlanId = 2;
    private const int CreativePlanId = 3;
    private const int AddOnPlanId = 4;

    private static readonly DateTime Now = new(2026, 11, 10, 3, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryUnitOfWork _store = new();
    private readonly Mock<IAuditLogWriter> _auditLogWriter = new();
    private readonly PaymentReconciliationService _sut;
    private int _nextPaymentId = 100;

    public PaymentReconciliationServiceTests()
    {
        _store.Seed(new UserAccount { Id = ParentId, Role = UserRole.User, Email = "p1@x.com", Username = "parent_one", FullName = "Phụ huynh Một" });
        _store.Seed(new UserAccount { Id = AdminId, Role = UserRole.Administrator, Email = "admin@x.com", Username = "admin", FullName = "Quản trị" });
        _store.Seed(new SubscriptionPlan { Id = 1, Name = "Personal", PlanType = PlanType.Legacy, PriceVnd = 49000, AiCredits = 50, IsActive = false });
        _store.Seed(new SubscriptionPlan { Id = ReadingPlanId, Name = "Gói Đọc", PlanType = PlanType.Reading, PriceVnd = 49000, AiCredits = 0 });
        _store.Seed(new SubscriptionPlan { Id = CreativePlanId, Name = "Gói Sáng tạo", PlanType = PlanType.Creative, PriceVnd = 99000, AiCredits = 4 });
        _store.Seed(new SubscriptionPlan { Id = AddOnPlanId, Name = "Add-on", PlanType = PlanType.AddOn, PriceVnd = 29000, AiCredits = 2 });

        _sut = CreateSut(RealSubscriptionService());
    }

    private SubscriptionService RealSubscriptionService() => new(
        _store, new TokenQuotaService(_store), new Mock<INotificationService>().Object,
        NullLogger<SubscriptionService>.Instance);

    private PaymentReconciliationService CreateSut(ISubscriptionService subscriptionService) =>
        new(_store, subscriptionService, _auditLogWriter.Object);

    private PaymentTransaction SeedTransaction(
        int planId, PaymentStatus status = PaymentStatus.Paid, DateTime? paidAt = null, int payerUserId = ParentId)
    {
        var id = _nextPaymentId++;
        var plan = _store.Items<SubscriptionPlan>().Single(item => item.Id == planId);
        return _store.Seed(new PaymentTransaction
        {
            Id = id,
            PlanId = planId,
            PayerUserId = payerUserId,
            TransactionCode = $"SEPAYRECON{id:D5}",
            Amount = plan.PriceVnd,
            AiCredits = plan.AiCredits,
            Status = status,
            PaidAt = status == PaymentStatus.Paid ? paidAt ?? Now.AddHours(-1) : null,
            ExpiresAt = Now.AddHours(-1).AddMinutes(15),
            CreatedAt = Now.AddHours(-1).AddMinutes(-2)
        });
    }

    private PaymentReconciliationException SeedException(
        PaymentTransaction transaction, ReconciliationStatus status = ReconciliationStatus.Open, DateTime? createdAt = null) =>
        _store.Seed(new PaymentReconciliationException
        {
            PaymentId = transaction.Id, Status = status, Reason = "NotApplied", CreatedAt = createdAt ?? Now,
            ResolvedBy = status == ReconciliationStatus.Resolved ? AdminId : null,
            ResolvedAt = status == ReconciliationStatus.Resolved ? Now : null
        });

    private IReadOnlyList<PaymentReconciliationException> Exceptions => _store.Items<PaymentReconciliationException>();

    // ---------- DetectAsync ----------

    [Fact]
    public async Task Detect_PaidCreativeWithoutSubscription_CreatesOpenException()
    {
        var transaction = SeedTransaction(CreativePlanId);

        var created = await _sut.DetectAsync(Now);

        Assert.Equal(1, created);
        var exception = Assert.Single(Exceptions);
        Assert.Equal((transaction.Id, ReconciliationStatus.Open, "NotApplied"), (exception.PaymentId, exception.Status, exception.Reason));
        // Job chỉ ghi nhận, không tự áp gói.
        Assert.Empty(_store.Items<UserSubscription>());
        Assert.Empty(_store.Items<CreditLot>());
    }

    [Theory]
    [InlineData(AddOnPlanId)]
    [InlineData(1)]
    public async Task Detect_PaidCreditPlanWithoutLot_CreatesOpenException(int planId)
    {
        SeedTransaction(planId);

        await _sut.DetectAsync(Now);

        Assert.Single(Exceptions);
    }

    [Fact]
    public async Task Detect_UsesLatestCreditFailedAuditAsReason()
    {
        var transaction = SeedTransaction(CreativePlanId);
        _store.Seed(new AuditLog
        {
            Action = "PaymentCreditFailed", EntityType = nameof(PaymentTransaction), EntityId = transaction.Id,
            AfterState = "{\"reason\":\"ApplyPlanThrew\",\"error\":\"old\"}", OccurredAt = Now.AddMinutes(-50)
        });
        _store.Seed(new AuditLog
        {
            Action = "PaymentCreditFailed", EntityType = nameof(PaymentTransaction), EntityId = transaction.Id,
            AfterState = "{\"reason\":\"ApplyPlanThrew\",\"error\":\"db down\"}", OccurredAt = Now.AddMinutes(-40)
        });
        _store.Seed(new AuditLog
        {
            Action = "PaymentCreditFailed", EntityType = nameof(PaymentTransaction), EntityId = transaction.Id + 500,
            AfterState = "{\"error\":\"other transaction\"}", OccurredAt = Now.AddMinutes(-10)
        });

        await _sut.DetectAsync(Now);

        Assert.Contains("db down", Assert.Single(Exceptions).Reason);
    }

    [Fact]
    public async Task Detect_VeryLongAuditState_IsTruncatedToColumnLimit()
    {
        var transaction = SeedTransaction(CreativePlanId);
        _store.Seed(new AuditLog
        {
            Action = "PaymentCreditFailed", EntityType = nameof(PaymentTransaction), EntityId = transaction.Id,
            AfterState = new string('x', 2000), OccurredAt = Now.AddMinutes(-40)
        });

        await _sut.DetectAsync(Now);

        Assert.Equal(500, Assert.Single(Exceptions).Reason.Length);
    }

    [Fact]
    public async Task Detect_AppliedTransactions_AreIgnored()
    {
        var creative = SeedTransaction(CreativePlanId);
        var addOn = SeedTransaction(AddOnPlanId);
        var reading = SeedTransaction(ReadingPlanId);
        await RealSubscriptionService().ApplyPaidTransactionAsync(reading, Plan(ReadingPlanId), Now.AddHours(-1));
        _store.Seed(new UserSubscription
        {
            UserId = ParentId, PlanId = CreativePlanId, Status = SubscriptionStatus.Scheduled,
            StartAt = Now.AddDays(20), EndAt = Now.AddDays(50), PaymentId = creative.Id
        });
        _store.Seed(new CreditLot { UserId = ParentId, Source = CreditSource.AddOn, Total = 2, Remaining = 0, PaymentId = addOn.Id });

        var created = await _sut.DetectAsync(Now);

        Assert.Equal(0, created);
        Assert.Empty(Exceptions);
    }

    [Fact]
    public async Task Detect_PaidWithinLastFiveMinutes_IsIgnored()
    {
        SeedTransaction(CreativePlanId, paidAt: Now.AddMinutes(-4));

        await _sut.DetectAsync(Now);

        Assert.Empty(Exceptions);
    }

    [Theory]
    [InlineData(PaymentStatus.Pending)]
    [InlineData(PaymentStatus.Expired)]
    [InlineData(PaymentStatus.MismatchAmount)]
    public async Task Detect_NonPaidTransactions_AreIgnored(PaymentStatus status)
    {
        SeedTransaction(CreativePlanId, status);

        await _sut.DetectAsync(Now);

        Assert.Empty(Exceptions);
    }

    [Fact]
    public async Task Detect_DeletedPayer_IsIgnored()
    {
        _store.Seed(new UserAccount { Id = 2, Role = UserRole.User, Email = "deleted-2", IsDeleted = true });
        SeedTransaction(CreativePlanId, payerUserId: 2);

        await _sut.DetectAsync(Now);

        Assert.Empty(Exceptions);
    }

    [Fact]
    public async Task Detect_CreditPlanWithZeroCreditsSnapshot_IsIgnored()
    {
        var transaction = SeedTransaction(AddOnPlanId);
        transaction.AiCredits = 0;

        await _sut.DetectAsync(Now);

        Assert.Empty(Exceptions);
    }

    [Fact]
    public async Task Detect_Twice_CreatesOneException()
    {
        SeedTransaction(CreativePlanId);

        await _sut.DetectAsync(Now);
        var second = await _sut.DetectAsync(Now.AddMinutes(15));

        Assert.Equal(0, second);
        Assert.Single(Exceptions);
    }

    [Fact]
    public async Task Detect_ResolvedException_IsNotReopened()
    {
        SeedException(SeedTransaction(CreativePlanId), ReconciliationStatus.Resolved);

        await _sut.DetectAsync(Now);

        Assert.Equal(ReconciliationStatus.Resolved, Assert.Single(Exceptions).Status);
    }

    // ---------- ResolveAsync ----------

    [Fact]
    public async Task Resolve_OpenException_AppliesPlanAndMarksResolved()
    {
        var transaction = SeedTransaction(CreativePlanId);
        await _sut.DetectAsync(Now);
        var exception = Assert.Single(Exceptions);

        var result = await _sut.ResolveAsync(AdminId, exception.Id);

        var subscription = Assert.Single(_store.Items<UserSubscription>());
        Assert.Equal(
            (SubscriptionStatus.Active, transaction.PaidAt!.Value, transaction.PaidAt.Value.AddMonths(1)),
            (subscription.Status, subscription.StartAt, subscription.EndAt));
        Assert.Equal(4, Assert.Single(_store.Items<CreditLot>()).Remaining);
        Assert.Equal((ReconciliationStatus.Resolved, (int?)AdminId), (exception.Status, exception.ResolvedBy));
        Assert.NotNull(exception.ResolvedAt);
        Assert.Equal(("Resolved", "Quản trị", transaction.TransactionCode, "p1@x.com", "Gói Sáng tạo"),
            (result.Status, result.ResolvedByName, result.Transaction.TransactionCode, result.Transaction.PayerEmail, result.Transaction.PlanName));
        _auditLogWriter.Verify(
            writer => writer.LogAsync(
                AdminId, "PaymentReconciliationResolved", nameof(PaymentReconciliationException), exception.Id,
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Resolve_AlreadyResolved_ThrowsConflictAndChangesNothing()
    {
        SeedTransaction(CreativePlanId);
        await _sut.DetectAsync(Now);
        var exception = Assert.Single(Exceptions);
        await _sut.ResolveAsync(AdminId, exception.Id);

        await Assert.ThrowsAsync<ConflictException>(() => _sut.ResolveAsync(AdminId, exception.Id));

        Assert.Single(_store.Items<UserSubscription>());
        Assert.Single(_store.Items<CreditLot>());
    }

    [Fact]
    public async Task Resolve_PlanAlreadyAppliedMeanwhile_MarksResolvedWithoutDoubleGrant()
    {
        var transaction = SeedTransaction(AddOnPlanId);
        var exception = SeedException(transaction);
        await RealSubscriptionService().ApplyPaidTransactionAsync(transaction, Plan(AddOnPlanId), Now);

        await _sut.ResolveAsync(AdminId, exception.Id);

        Assert.Equal(ReconciliationStatus.Resolved, exception.Status);
        Assert.Equal(2, Assert.Single(_store.Items<CreditLot>()).Remaining);
    }

    [Fact]
    public async Task Resolve_ApplyStillFails_KeepsOpenUpdatesReasonAndThrowsConflict()
    {
        var failing = new Mock<ISubscriptionService>();
        failing.Setup(service => service.ApplyPaidTransactionAsync(
                It.IsAny<PaymentTransaction>(), It.IsAny<SubscriptionPlan>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db still down"));
        var exception = SeedException(SeedTransaction(CreativePlanId));

        var error = await Assert.ThrowsAsync<ConflictException>(() => CreateSut(failing.Object).ResolveAsync(AdminId, exception.Id));

        Assert.Contains("db still down", error.Message);
        Assert.Equal(ReconciliationStatus.Open, exception.Status);
        Assert.Contains("db still down", exception.Reason);
        Assert.Null(exception.ResolvedAt);
        _auditLogWriter.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Resolve_NotFound_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.ResolveAsync(AdminId, 999));
    }

    // ---------- ListAsync ----------

    [Fact]
    public async Task List_DefaultsToOpen_NewestFirst_WithTransactionAndPayer()
    {
        var older = SeedException(SeedTransaction(CreativePlanId), createdAt: Now.AddHours(-3));
        var newer = SeedException(SeedTransaction(AddOnPlanId), createdAt: Now.AddHours(-1));
        SeedException(SeedTransaction(ReadingPlanId), ReconciliationStatus.Resolved);

        var result = await _sut.ListAsync(null, pageIndex: 1, pageSize: 20);

        Assert.Equal([newer.Id, older.Id], result.Items.Select(item => item.Id));
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(
            ("Open", "Add-on", 29000, ParentId, "Phụ huynh Một"),
            (result.Items[0].Status, result.Items[0].Transaction.PlanName, result.Items[0].Transaction.Amount,
                result.Items[0].Transaction.PayerUserId, result.Items[0].Transaction.PayerName));
    }

    [Fact]
    public async Task List_ByResolvedStatus_ShowsResolver()
    {
        SeedException(SeedTransaction(CreativePlanId));
        SeedException(SeedTransaction(ReadingPlanId), ReconciliationStatus.Resolved);

        var result = await _sut.ListAsync("resolved", pageIndex: 1, pageSize: 20);

        var only = Assert.Single(result.Items);
        Assert.Equal(("Resolved", "Quản trị"), (only.Status, only.ResolvedByName));
    }

    [Fact]
    public async Task List_InvalidStatus_ThrowsBadRequest()
    {
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.ListAsync("Closed", pageIndex: 1, pageSize: 20));
    }

    private SubscriptionPlan Plan(int id) => _store.Items<SubscriptionPlan>().Single(plan => plan.Id == id);
}
