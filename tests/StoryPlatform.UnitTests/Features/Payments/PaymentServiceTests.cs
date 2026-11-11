using Moq;
using StoryPlatform.Application.Abstractions.Payments;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Notifications.DTOs;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Application.Features.Payments.DTOs;
using StoryPlatform.Application.Features.Payments.Interfaces;
using StoryPlatform.Application.Features.Payments.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.UnitTests.Features.TokenQuota;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Payments;

public class PaymentServiceTests
{
    private const int ParentId = 1;
    private const int OtherParentId = 2;
    private const int AdminId = 9;
    private const int LegacyPlanId = 1;
    private const int ReadingPlanId = 2;
    private const int CreativePlanId = 3;
    private const int AddOnPlanId = 4;
    private const int CreatorPackPlanId = 5;
    private const int CreatorId = 7;
    private const string AddOnLockedReason = "Hãy đăng ký Gói Đọc hoặc Gói Sáng tạo trước khi mua Add-on.";

    private readonly InMemoryUnitOfWork _store = new();
    private readonly Mock<IAuditLogWriter> _auditLogWriter = new();
    private readonly Mock<INotificationService> _notificationService = new();
    private readonly Mock<ISePayQrUrlBuilder> _qrUrlBuilder = new();
    private readonly Mock<ISePayWebhookAuthenticator> _webhookAuthenticator = new();
    private readonly Mock<ISubscriptionService> _subscriptionService = new();
    private readonly PaymentService _sut;
    private int _nextTransactionId = 1;

    public PaymentServiceTests()
    {
        _store.Seed(new UserAccount { Id = ParentId, Role = UserRole.User, Status = AccountStatus.Active, Email = "p1@x.com", Username = "parent_one", FullName = "Phụ huynh Một" });
        _store.Seed(new UserAccount { Id = OtherParentId, Role = UserRole.User, Status = AccountStatus.Active, Email = "p2@x.com", Username = "parent_two", FullName = "Phụ huynh Hai" });
        _store.Seed(new UserAccount { Id = AdminId, Role = UserRole.Administrator, Email = "admin@x.com", Username = "admin", FullName = "Quản trị" });
        _store.Seed(new UserAccount { Id = CreatorId, Role = UserRole.ContentCreator, Status = AccountStatus.Active, Email = "c@x.com", Username = "creator", FullName = "Tác giả" });
        _store.Seed(new SubscriptionPlan { Id = LegacyPlanId, Name = "Personal", PlanType = PlanType.Legacy, PriceVnd = 49000, AiCredits = 50, IsActive = false });
        _store.Seed(new SubscriptionPlan { Id = ReadingPlanId, Name = "Gói Đọc", PlanType = PlanType.Reading, PriceVnd = 49000, AiCredits = 0 });
        _store.Seed(new SubscriptionPlan { Id = CreativePlanId, Name = "Gói Sáng tạo", PlanType = PlanType.Creative, PriceVnd = 99000, AiCredits = 4 });
        _store.Seed(new SubscriptionPlan { Id = AddOnPlanId, Name = "Add-on", PlanType = PlanType.AddOn, PriceVnd = 29000, AiCredits = 2 });
        _store.Seed(new SubscriptionPlan { Id = CreatorPackPlanId, Name = "Gói Creator", PlanType = PlanType.CreatorPack, Audience = PlanAudience.Creator, PriceVnd = 99000, AiCredits = 6, SubmitCredits = 20 });

        _notificationService
            .Setup(service => service.CreateAsync(
                It.IsAny<int>(), It.IsAny<NotificationType>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationDto());
        _qrUrlBuilder.Setup(builder => builder.BuildQrCodeUrl(It.IsAny<int>(), It.IsAny<string>()))
            .Returns("https://qr.sepay.vn/img?fake=1");
        _webhookAuthenticator.Setup(auth => auth.IsValid(It.IsAny<string?>())).Returns(true);

        _sut = new PaymentService(
            _store, _auditLogWriter.Object, _notificationService.Object,
            _qrUrlBuilder.Object, _webhookAuthenticator.Object, _subscriptionService.Object);
    }

    private SubscriptionPlan Plan(int id) => _store.Items<SubscriptionPlan>().Single(plan => plan.Id == id);

    private void HasActivePlan(int userId = ParentId) =>
        _subscriptionService
            .Setup(service => service.GetActiveAsync(userId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserSubscription
            {
                UserId = userId, PlanId = ReadingPlanId, Status = SubscriptionStatus.Active,
                StartAt = DateTime.UtcNow.AddDays(-5), EndAt = DateTime.UtcNow.AddDays(25)
            });

    private PaymentTransaction SeedTransaction(
        int planId, PaymentStatus status = PaymentStatus.Pending, int payerUserId = ParentId,
        DateTime? expiresAt = null, DateTime? createdAt = null, string? code = null)
    {
        var id = _nextTransactionId++;
        return _store.Seed(new PaymentTransaction
        {
            Id = id,
            PlanId = planId,
            PayerUserId = payerUserId,
            PayerUser = _store.Items<UserAccount>().Single(user => user.Id == payerUserId),
            TransactionCode = code ?? $"SEPAYTEST{id:D6}",
            Amount = Plan(planId).PriceVnd,
            Status = status,
            QrCodeUrl = "https://qr.sepay.vn/img?seed=1",
            ExpiresAt = expiresAt ?? DateTime.UtcNow.AddMinutes(10),
            CreatedAt = createdAt ?? DateTime.UtcNow
        });
    }

    private void VerifyPlanApplied(PaymentTransaction transaction, Times times) =>
        _subscriptionService.Verify(
            service => service.ApplyPaidTransactionAsync(
                transaction, It.Is<SubscriptionPlan>(plan => plan.Id == transaction.PlanId),
                It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            times);

    private void VerifyNoPlanApplied() =>
        _subscriptionService.Verify(
            service => service.ApplyPaidTransactionAsync(
                It.IsAny<PaymentTransaction>(), It.IsAny<SubscriptionPlan>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);

    private void VerifyAudit(string action, Times times) =>
        _auditLogWriter.Verify(
            writer => writer.LogAsync(
                It.IsAny<int?>(), action, nameof(PaymentTransaction), It.IsAny<int>(),
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()),
            times);

    // ---------- ListActivePlansAsync ----------

    [Fact]
    public async Task ListActivePlansAsync_ExcludesLegacyAndInactive()
    {
        _store.Seed(new SubscriptionPlan { Id = 5, Name = "Gói thử", PlanType = PlanType.Creative, PriceVnd = 1, AiCredits = 1, IsActive = false });
        _store.Seed(new SubscriptionPlan { Id = 6, Name = "Personal 2", PlanType = PlanType.Legacy, PriceVnd = 1, AiCredits = 1, IsActive = true });

        var result = await _sut.ListActivePlansAsync(ParentId);

        Assert.Equal(
            [(ReadingPlanId, "Reading", 49000, 0), (CreativePlanId, "Creative", 99000, 4), (AddOnPlanId, "AddOn", 29000, 2)],
            result.Select(plan => (plan.Id, plan.PlanType, plan.PriceVnd, plan.AiCredits)));
    }

    [Fact]
    public async Task ListActivePlansAsync_FreeAccount_AddOnIsLocked()
    {
        var result = await _sut.ListActivePlansAsync(ParentId);

        var addOn = result.Single(plan => plan.PlanType == "AddOn");
        Assert.Equal((false, AddOnLockedReason), (addOn.IsPurchasable, addOn.LockedReason));
        Assert.All(
            result.Where(plan => plan.PlanType != "AddOn"),
            plan => Assert.Equal((true, (string?)null), (plan.IsPurchasable, plan.LockedReason)));
    }

    [Fact]
    public async Task ListActivePlansAsync_WithActivePlan_AddOnIsPurchasable()
    {
        HasActivePlan();

        var result = await _sut.ListActivePlansAsync(ParentId);

        var addOn = result.Single(plan => plan.PlanType == "AddOn");
        Assert.Equal((true, (string?)null), (addOn.IsPurchasable, addOn.LockedReason));
    }

    // ---------- CreateTransactionAsync ----------

    [Fact]
    public async Task CreateTransactionAsync_PlanNotFound_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _sut.CreateTransactionAsync(ParentId, new CreatePaymentTransactionRequestDto { PlanId = 999 }));
    }

    [Fact]
    public async Task CreateTransactionAsync_PlanInactive_ThrowsBadRequest()
    {
        Plan(CreativePlanId).IsActive = false;

        await Assert.ThrowsAsync<BadRequestException>(
            () => _sut.CreateTransactionAsync(ParentId, new CreatePaymentTransactionRequestDto { PlanId = CreativePlanId }));
    }

    [Fact]
    public async Task CreateTransactionAsync_LegacyPlan_ThrowsBadRequest()
    {
        Plan(LegacyPlanId).IsActive = true;

        await Assert.ThrowsAsync<BadRequestException>(
            () => _sut.CreateTransactionAsync(ParentId, new CreatePaymentTransactionRequestDto { PlanId = LegacyPlanId }));

        Assert.Empty(_store.Items<PaymentTransaction>());
    }

    [Theory]
    [InlineData(UserRole.Administrator)]
    [InlineData(UserRole.Moderator)]
    public async Task CreateTransactionAsync_NonParentPayer_ThrowsForbidden(UserRole role)
    {
        _store.Seed(new UserAccount { Id = 50, Role = role, Email = "staff@x.com" });

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _sut.CreateTransactionAsync(50, new CreatePaymentTransactionRequestDto { PlanId = ReadingPlanId }));
    }

    [Fact]
    public async Task CreateTransactionAsync_ValidCreative_CreatesPendingTransactionWithQr()
    {
        var before = DateTime.UtcNow;

        var result = await _sut.CreateTransactionAsync(ParentId, new CreatePaymentTransactionRequestDto { PlanId = CreativePlanId });

        var stored = Assert.Single(_store.Items<PaymentTransaction>());
        Assert.Equal((ParentId, CreativePlanId, 99000, PaymentStatus.Pending), (stored.PayerUserId, stored.PlanId, stored.Amount, stored.Status));
        Assert.StartsWith("SEPAY", stored.TransactionCode);
        Assert.Equal(15, stored.TransactionCode.Length);
        Assert.InRange(stored.ExpiresAt, before.AddMinutes(15), DateTime.UtcNow.AddMinutes(15));
        Assert.Equal(
            ("Gói Sáng tạo", "Creative", "Pending", "https://qr.sepay.vn/img?fake=1"),
            (result.PlanName, result.PlanType, result.Status, result.QrCodeUrl));
        _qrUrlBuilder.Verify(builder => builder.BuildQrCodeUrl(99000, stored.TransactionCode), Times.Once);
    }

    [Fact]
    public async Task CreateTransactionAsync_SnapshotsPlanCredits()
    {
        await _sut.CreateTransactionAsync(ParentId, new CreatePaymentTransactionRequestDto { PlanId = CreativePlanId });

        Assert.Equal(4, Assert.Single(_store.Items<PaymentTransaction>()).AiCredits);
    }

    [Fact]
    public async Task CreateTransactionAsync_PendingCreativeExists_BuyingReading_ThrowsConflict()
    {
        SeedTransaction(CreativePlanId);

        await Assert.ThrowsAsync<ConflictException>(
            () => _sut.CreateTransactionAsync(ParentId, new CreatePaymentTransactionRequestDto { PlanId = ReadingPlanId }));

        Assert.Single(_store.Items<PaymentTransaction>());
    }

    [Fact]
    public async Task CreateTransactionAsync_PendingAddOnExists_BuyingReading_Succeeds()
    {
        SeedTransaction(AddOnPlanId);

        var result = await _sut.CreateTransactionAsync(ParentId, new CreatePaymentTransactionRequestDto { PlanId = ReadingPlanId });

        Assert.Equal("Reading", result.PlanType);
    }

    [Fact]
    public async Task CreateTransactionAsync_AnotherParentsPendingPlan_DoesNotBlock()
    {
        SeedTransaction(CreativePlanId, payerUserId: OtherParentId);

        var result = await _sut.CreateTransactionAsync(ParentId, new CreatePaymentTransactionRequestDto { PlanId = CreativePlanId });

        Assert.Equal("Pending", result.Status);
    }

    [Fact]
    public async Task CreateTransactionAsync_StalePendingPastExpiry_DoesNotBlock()
    {
        SeedTransaction(ReadingPlanId, expiresAt: DateTime.UtcNow.AddMinutes(-1));

        var result = await _sut.CreateTransactionAsync(ParentId, new CreatePaymentTransactionRequestDto { PlanId = ReadingPlanId });

        Assert.Equal("Pending", result.Status);
        Assert.Equal(2, _store.Items<PaymentTransaction>().Count);
    }

    [Fact]
    public async Task CreateTransactionAsync_AddOn_NoActivePlan_ThrowsConflict()
    {
        var error = await Assert.ThrowsAsync<ConflictException>(
            () => _sut.CreateTransactionAsync(ParentId, new CreatePaymentTransactionRequestDto { PlanId = AddOnPlanId }));

        Assert.Equal(AddOnLockedReason, error.Message);
        Assert.Empty(_store.Items<PaymentTransaction>());
    }

    [Fact]
    public async Task CreateTransactionAsync_AddOn_PendingAddOnExists_ThrowsConflict()
    {
        HasActivePlan();
        SeedTransaction(AddOnPlanId);

        await Assert.ThrowsAsync<ConflictException>(
            () => _sut.CreateTransactionAsync(ParentId, new CreatePaymentTransactionRequestDto { PlanId = AddOnPlanId }));
    }

    [Fact]
    public async Task CreateTransactionAsync_AddOn_WithActivePlanAndPendingPlanPurchase_CreatesPending29000()
    {
        HasActivePlan();
        SeedTransaction(CreativePlanId);

        var result = await _sut.CreateTransactionAsync(ParentId, new CreatePaymentTransactionRequestDto { PlanId = AddOnPlanId });

        Assert.Equal((29000, "Pending", "AddOn"), (result.Amount, result.Status, result.PlanType));
    }

    // ---------- Đối tượng của gói: phụ huynh / Creator (2f) ----------

    [Fact]
    public async Task ListActivePlansAsync_Parent_DoesNotSeeTheCreatorPack()
    {
        var result = await _sut.ListActivePlansAsync(ParentId);

        Assert.Equal(["Reading", "Creative", "AddOn"], result.Select(plan => plan.PlanType));
        Assert.All(result, plan => Assert.Equal("User", plan.Audience));
    }

    [Fact]
    public async Task ListActivePlansAsync_Creator_SeesOnlyTheCreatorPack()
    {
        var result = await _sut.ListActivePlansAsync(CreatorId);

        var pack = Assert.Single(result);
        Assert.Equal(
            ("CreatorPack", "Creator", 99000, 6, 20, true, (string?)null),
            (pack.PlanType, pack.Audience, pack.PriceVnd, pack.AiCredits, pack.SubmitCredits, pack.IsPurchasable, pack.LockedReason));
    }

    [Theory]
    [InlineData(UserRole.Administrator)]
    [InlineData(UserRole.Moderator)]
    public async Task ListActivePlansAsync_StaffSeeNoPlansForSale(UserRole role)
    {
        _store.Seed(new UserAccount { Id = 60, Role = role, Status = AccountStatus.Active, Email = "s@x.com" });

        Assert.Empty(await _sut.ListActivePlansAsync(60));
    }

    [Fact]
    public async Task CreateTransactionAsync_Creator_BuysTheCreatorPack_WithCreditsSnapshotted()
    {
        var result = await _sut.CreateTransactionAsync(CreatorId, new CreatePaymentTransactionRequestDto { PlanId = CreatorPackPlanId });

        var stored = Assert.Single(_store.Items<PaymentTransaction>());
        Assert.Equal((CreatorId, 99000, (int?)6, (int?)20, PaymentStatus.Pending),
            (stored.PayerUserId, stored.Amount, stored.AiCredits, stored.SubmitCredits, stored.Status));
        Assert.Equal(("Gói Creator", "CreatorPack"), (result.PlanName, result.PlanType));
    }

    [Fact]
    public async Task CreateTransactionAsync_ParentBuyingTheCreatorPack_ThrowsForbidden()
    {
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _sut.CreateTransactionAsync(ParentId, new CreatePaymentTransactionRequestDto { PlanId = CreatorPackPlanId }));

        Assert.Empty(_store.Items<PaymentTransaction>());
    }

    [Theory]
    [InlineData(ReadingPlanId)]
    [InlineData(CreativePlanId)]
    [InlineData(AddOnPlanId)]
    public async Task CreateTransactionAsync_CreatorBuyingAParentPlan_ThrowsForbidden(int planId)
    {
        HasActivePlan(CreatorId);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _sut.CreateTransactionAsync(CreatorId, new CreatePaymentTransactionRequestDto { PlanId = planId }));
    }

    [Theory]
    [InlineData(UserRole.ContentCreator, AccountStatus.Registered, CreatorPackPlanId)]
    [InlineData(UserRole.ContentCreator, AccountStatus.Suspended, CreatorPackPlanId)]
    [InlineData(UserRole.User, AccountStatus.Registered, ReadingPlanId)]
    [InlineData(UserRole.User, AccountStatus.Suspended, ReadingPlanId)]
    public async Task CreateTransactionAsync_AccountNotActive_ThrowsForbidden(UserRole role, AccountStatus status, int planId)
    {
        _store.Seed(new UserAccount { Id = 61, Role = role, Status = status, Email = "n@x.com" });

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _sut.CreateTransactionAsync(61, new CreatePaymentTransactionRequestDto { PlanId = planId }));
    }

    [Fact]
    public async Task CreateTransactionAsync_SecondPendingCreatorPack_ThrowsConflict()
    {
        await _sut.CreateTransactionAsync(CreatorId, new CreatePaymentTransactionRequestDto { PlanId = CreatorPackPlanId });

        await Assert.ThrowsAsync<ConflictException>(
            () => _sut.CreateTransactionAsync(CreatorId, new CreatePaymentTransactionRequestDto { PlanId = CreatorPackPlanId }));
    }

    [Fact]
    public async Task UpdatePlanAsync_CanChangeSubmitCredits_AndKeepsThemWhenOmitted()
    {
        await _sut.UpdatePlanAsync(AdminId, CreatorPackPlanId, new UpdateSubscriptionPlanRequestDto
        {
            Name = "Gói Creator", PriceVnd = 109000, AiCredits = 8, IsActive = true, SubmitCredits = 25
        });
        var kept = await _sut.UpdatePlanAsync(AdminId, CreatorPackPlanId, new UpdateSubscriptionPlanRequestDto
        {
            Name = "Gói Creator", PriceVnd = 109000, AiCredits = 8, IsActive = true
        });

        Assert.Equal((25, 25), (Plan(CreatorPackPlanId).SubmitCredits, kept.SubmitCredits));
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.UpdatePlanAsync(AdminId, CreatorPackPlanId,
            new UpdateSubscriptionPlanRequestDto { Name = "Gói Creator", PriceVnd = 109000, AiCredits = 8, IsActive = true, SubmitCredits = -1 }));
    }

    // ---------- HandleWebhookAsync ----------

    [Fact]
    public async Task HandleWebhookAsync_InvalidAuth_ThrowsUnauthorized()
    {
        var transaction = SeedTransaction(CreativePlanId);
        _webhookAuthenticator.Setup(auth => auth.IsValid(It.IsAny<string?>())).Returns(false);

        await Assert.ThrowsAsync<UnauthorizedException>(() => _sut.HandleWebhookAsync(
            "bad", new SePayWebhookPayloadDto { Content = transaction.TransactionCode, TransferAmount = 99000 }));

        Assert.Equal(PaymentStatus.Pending, transaction.Status);
        VerifyNoPlanApplied();
    }

    [Fact]
    public async Task HandleWebhookAsync_NoMatchingTransactionCode_NoOp()
    {
        var transaction = SeedTransaction(CreativePlanId);

        await _sut.HandleWebhookAsync("ok", new SePayWebhookPayloadDto { Content = "chuyen khoan khac", TransferAmount = 99000 });

        Assert.Equal(PaymentStatus.Pending, transaction.Status);
        VerifyNoPlanApplied();
    }

    [Fact]
    public async Task HandleWebhookAsync_AmountMatches_MarksPaidNotifiesAndAppliesPlan()
    {
        var transaction = SeedTransaction(CreativePlanId, code: "SEPAYABCDEF0123");

        await _sut.HandleWebhookAsync("ok", new SePayWebhookPayloadDto
        {
            Content = "MBVCB 123 sepayabcdef0123 chuyen tien", TransferAmount = 99000, ReferenceCode = "FT-001"
        });

        Assert.Equal((PaymentStatus.Paid, "FT-001"), (transaction.Status, transaction.SepayTransactionId));
        Assert.NotNull(transaction.PaidAt);
        _notificationService.Verify(
            service => service.CreateAsync(
                ParentId, NotificationType.PaymentConfirmed, It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        VerifyAudit("PaymentPaidViaWebhook", Times.Once());
        VerifyPlanApplied(transaction, Times.Once());
    }

    [Fact]
    public async Task HandleWebhookAsync_AmountMismatch_SetsMismatchWithoutNotificationOrPlan()
    {
        var transaction = SeedTransaction(CreativePlanId);

        await _sut.HandleWebhookAsync("ok", new SePayWebhookPayloadDto { Content = transaction.TransactionCode, TransferAmount = 90000 });

        Assert.Equal(PaymentStatus.MismatchAmount, transaction.Status);
        Assert.Null(transaction.PaidAt);
        _notificationService.VerifyNoOtherCalls();
        VerifyAudit("PaymentMismatchAmount", Times.Once());
        VerifyNoPlanApplied();
    }

    [Fact]
    public async Task HandleWebhookAsync_TransactionAlreadyPaid_IsIdempotentNoOp()
    {
        var transaction = SeedTransaction(CreativePlanId, PaymentStatus.Paid);

        await _sut.HandleWebhookAsync("ok", new SePayWebhookPayloadDto { Content = transaction.TransactionCode, TransferAmount = 99000 });

        _notificationService.VerifyNoOtherCalls();
        VerifyNoPlanApplied();
    }

    [Fact]
    public async Task HandleWebhookAsync_ApplyThrows_StaysPaidAndWritesCreditFailedAudit()
    {
        var transaction = SeedTransaction(CreativePlanId);
        _subscriptionService
            .Setup(service => service.ApplyPaidTransactionAsync(
                It.IsAny<PaymentTransaction>(), It.IsAny<SubscriptionPlan>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));
        object? auditState = null;
        _auditLogWriter
            .Setup(writer => writer.LogAsync(
                It.IsAny<int?>(), "PaymentCreditFailed", nameof(PaymentTransaction), transaction.Id,
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Callback((int? _, string _, string _, int _, object? _, object? after, CancellationToken _) => auditState = after);

        await _sut.HandleWebhookAsync("ok", new SePayWebhookPayloadDto { Content = transaction.TransactionCode, TransferAmount = 99000 });

        Assert.Equal(PaymentStatus.Paid, transaction.Status);
        Assert.Contains("ApplyPlanThrew", System.Text.Json.JsonSerializer.Serialize(auditState));
        Assert.Contains("db down", System.Text.Json.JsonSerializer.Serialize(auditState));
    }

    [Fact]
    public async Task HandleWebhookAsync_PlanMissing_StaysPaidAndWritesCreditFailedAudit()
    {
        var transaction = SeedTransaction(CreativePlanId);
        Plan(CreativePlanId).IsDeleted = true;

        await _sut.HandleWebhookAsync("ok", new SePayWebhookPayloadDto { Content = transaction.TransactionCode, TransferAmount = 99000 });

        Assert.Equal(PaymentStatus.Paid, transaction.Status);
        VerifyAudit("PaymentCreditFailed", Times.Once());
        VerifyNoPlanApplied();
    }

    // ---------- ListMismatchedAsync / MarkPaidManuallyAsync ----------

    [Fact]
    public async Task ListMismatchedAsync_ReturnsOnlyMismatchedTransactions()
    {
        SeedTransaction(ReadingPlanId);
        var mismatched = SeedTransaction(CreativePlanId, PaymentStatus.MismatchAmount);
        SeedTransaction(AddOnPlanId, PaymentStatus.Paid);

        var result = await _sut.ListMismatchedAsync();

        var only = Assert.Single(result);
        Assert.Equal((mismatched.Id, "Gói Sáng tạo", "MismatchAmount"), (only.Id, only.PlanName, only.Status));
    }

    [Fact]
    public async Task MarkPaidManuallyAsync_NotFound_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.MarkPaidManuallyAsync(AdminId, 999));
    }

    [Fact]
    public async Task MarkPaidManuallyAsync_AlreadyPaid_ThrowsConflict()
    {
        var transaction = SeedTransaction(CreativePlanId, PaymentStatus.Paid);

        await Assert.ThrowsAsync<ConflictException>(() => _sut.MarkPaidManuallyAsync(AdminId, transaction.Id));

        VerifyNoPlanApplied();
    }

    [Theory]
    [InlineData(PaymentStatus.MismatchAmount)]
    [InlineData(PaymentStatus.Expired)]
    [InlineData(PaymentStatus.Pending)]
    public async Task MarkPaidManuallyAsync_NotYetPaid_SetsPaidWritesAuditNotifiesAndAppliesPlan(PaymentStatus status)
    {
        var transaction = SeedTransaction(CreativePlanId, status);

        var result = await _sut.MarkPaidManuallyAsync(AdminId, transaction.Id);

        Assert.Equal(PaymentStatus.Paid, transaction.Status);
        Assert.NotNull(transaction.PaidAt);
        Assert.Equal(("Paid", "Gói Sáng tạo"), (result.Status, result.PlanName));
        _auditLogWriter.Verify(
            writer => writer.LogAsync(
                AdminId, "PaymentMarkedPaidManually", nameof(PaymentTransaction), transaction.Id,
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _notificationService.Verify(
            service => service.CreateAsync(
                ParentId, NotificationType.PaymentConfirmed, It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        VerifyPlanApplied(transaction, Times.Once());
    }

    [Fact]
    public async Task MarkPaidManuallyAsync_ApplyThrows_StaysPaidReturnsDtoAndWritesAuditLog()
    {
        var transaction = SeedTransaction(AddOnPlanId, PaymentStatus.MismatchAmount);
        _subscriptionService
            .Setup(service => service.ApplyPaidTransactionAsync(
                It.IsAny<PaymentTransaction>(), It.IsAny<SubscriptionPlan>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        var result = await _sut.MarkPaidManuallyAsync(AdminId, transaction.Id);

        Assert.Equal(("Paid", "Add-on"), (result.Status, result.PlanName));
        VerifyAudit("PaymentCreditFailed", Times.Once());
    }

    // ---------- Admin: ListAllTransactionsAsync ----------

    [Fact]
    public async Task ListAllTransactionsAsync_ReturnsEveryPayersNewestFirstWithPayerInfo()
    {
        var now = DateTime.UtcNow;
        var older = SeedTransaction(ReadingPlanId, PaymentStatus.Paid, createdAt: now.AddDays(-3));
        var newer = SeedTransaction(CreativePlanId, payerUserId: OtherParentId, createdAt: now.AddDays(-1));

        var result = await _sut.ListAllTransactionsAsync(new AdminTransactionQueryDto());

        Assert.Equal([newer.Id, older.Id], result.Items.Select(item => item.Id));
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(
            (OtherParentId, "p2@x.com", "Phụ huynh Hai", "Gói Sáng tạo", "Creative"),
            (result.Items[0].PayerUserId, result.Items[0].PayerEmail, result.Items[0].PayerName, result.Items[0].PlanName, result.Items[0].PlanType));
    }

    [Fact]
    public async Task ListAllTransactionsAsync_FiltersByStatus()
    {
        var pending = SeedTransaction(CreativePlanId);
        SeedTransaction(ReadingPlanId, PaymentStatus.Paid);
        SeedTransaction(AddOnPlanId, PaymentStatus.Expired);

        var result = await _sut.ListAllTransactionsAsync(new AdminTransactionQueryDto { Status = "pending" });

        Assert.Equal(pending.Id, Assert.Single(result.Items).Id);
    }

    [Fact]
    public async Task ListAllTransactionsAsync_InvalidStatus_ThrowsBadRequest()
    {
        await Assert.ThrowsAsync<BadRequestException>(
            () => _sut.ListAllTransactionsAsync(new AdminTransactionQueryDto { Status = "Done" }));
    }

    [Fact]
    public async Task ListAllTransactionsAsync_StalePendingKeepsStoredStatusAndQr()
    {
        SeedTransaction(CreativePlanId, expiresAt: DateTime.UtcNow.AddMinutes(-30));

        var result = await _sut.ListAllTransactionsAsync(new AdminTransactionQueryDto());

        var only = Assert.Single(result.Items);
        Assert.Equal("Pending", only.Status);
        Assert.NotNull(only.QrCodeUrl);
    }

    [Theory]
    [InlineData("abcdef")]
    [InlineData("  SEPAYABC  ")]
    [InlineData("P2@X")]
    [InlineData("parent_TWO")]
    public async Task ListAllTransactionsAsync_SearchMatchesCodeOrPayerCaseInsensitive(string search)
    {
        var match = SeedTransaction(CreativePlanId, payerUserId: OtherParentId, code: "SEPAYABCDEF0123");
        SeedTransaction(ReadingPlanId, code: "SEPAYZZZZZZ9999");

        var result = await _sut.ListAllTransactionsAsync(new AdminTransactionQueryDto { Search = search });

        Assert.Equal(match.Id, Assert.Single(result.Items).Id);
    }

    [Theory]
    [InlineData("%", 0)]
    [InlineData("pa_ent", 0)]
    [InlineData("   ", 2)]
    [InlineData("", 2)]
    public async Task ListAllTransactionsAsync_SearchWithWildcardsOrBlank_IsLiteral(string search, int expected)
    {
        SeedTransaction(CreativePlanId, payerUserId: OtherParentId, code: "SEPAYABCDEF0123");
        SeedTransaction(ReadingPlanId, code: "SEPAYZZZZZZ9999");

        var result = await _sut.ListAllTransactionsAsync(new AdminTransactionQueryDto { Search = search });

        Assert.Equal(expected, result.TotalCount);
    }

    [Fact]
    public async Task ListAllTransactionsAsync_ClampsPaging()
    {
        for (var index = 0; index < 3; index++) SeedTransaction(ReadingPlanId, PaymentStatus.Expired);

        var result = await _sut.ListAllTransactionsAsync(new AdminTransactionQueryDto { PageIndex = 0, PageSize = 100000 });

        Assert.Equal((1, 100, 3), (result.PageIndex, result.PageSize, result.Items.Count));
    }

    // ---------- Admin: gói ----------

    [Fact]
    public async Task ListAllPlansAsync_IncludesInactiveAndLegacy()
    {
        var result = await _sut.ListAllPlansAsync();

        Assert.Equal(
            ["1:Legacy:User:False", "2:Reading:User:True", "3:Creative:User:True", "4:AddOn:User:True", "5:CreatorPack:Creator:True"],
            result.Select(plan => $"{plan.Id}:{plan.PlanType}:{plan.Audience}:{plan.IsActive}"));
    }

    [Fact]
    public async Task UpdatePlanAsync_ChangesFieldsAndWritesAudit()
    {
        var result = await _sut.UpdatePlanAsync(AdminId, CreativePlanId, new UpdateSubscriptionPlanRequestDto
        {
            Name = "  Gói Sáng tạo Plus ", PriceVnd = 109000, AiCredits = 5, IsActive = true
        });

        var plan = Plan(CreativePlanId);
        Assert.Equal(("Gói Sáng tạo Plus", 109000, 5, true, PlanType.Creative), (plan.Name, plan.PriceVnd, plan.AiCredits, plan.IsActive, plan.PlanType));
        Assert.NotNull(plan.UpdatedAt);
        Assert.Equal(("Gói Sáng tạo Plus", 109000, 5, "Creative"), (result.Name, result.PriceVnd, result.AiCredits, result.PlanType));
        _auditLogWriter.Verify(
            writer => writer.LogAsync(
                AdminId, "SubscriptionPlanUpdated", nameof(SubscriptionPlan), CreativePlanId,
                It.IsNotNull<object?>(), It.IsNotNull<object?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData("", 99000, 4)]
    [InlineData("   ", 99000, 4)]
    [InlineData("Gói", 0, 4)]
    [InlineData("Gói", -1, 4)]
    [InlineData("Gói", 99000, -1)]
    public async Task UpdatePlanAsync_InvalidInput_ThrowsBadRequestAndChangesNothing(string name, int price, int credits)
    {
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.UpdatePlanAsync(
            AdminId, CreativePlanId,
            new UpdateSubscriptionPlanRequestDto { Name = name, PriceVnd = price, AiCredits = credits, IsActive = true }));

        Assert.Equal(("Gói Sáng tạo", 99000, 4), (Plan(CreativePlanId).Name, Plan(CreativePlanId).PriceVnd, Plan(CreativePlanId).AiCredits));
    }

    [Fact]
    public async Task UpdatePlanAsync_NameLongerThan150_ThrowsBadRequest()
    {
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.UpdatePlanAsync(
            AdminId, CreativePlanId,
            new UpdateSubscriptionPlanRequestDto { Name = new string('a', 151), PriceVnd = 99000, AiCredits = 4, IsActive = true }));
    }

    [Fact]
    public async Task UpdatePlanAsync_ActivatingLegacyPlan_ThrowsBadRequest()
    {
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.UpdatePlanAsync(
            AdminId, LegacyPlanId,
            new UpdateSubscriptionPlanRequestDto { Name = "Personal", PriceVnd = 49000, AiCredits = 50, IsActive = true }));

        Assert.False(Plan(LegacyPlanId).IsActive);
    }

    [Fact]
    public async Task UpdatePlanAsync_PlanNotFound_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.UpdatePlanAsync(
            AdminId, 999, new UpdateSubscriptionPlanRequestDto { Name = "x", PriceVnd = 1, AiCredits = 0, IsActive = true }));
    }

    [Fact]
    public async Task UpdatePlanAsync_DeactivatedPlan_DisappearsFromBuyerList()
    {
        await _sut.UpdatePlanAsync(AdminId, ReadingPlanId, new UpdateSubscriptionPlanRequestDto
        {
            Name = "Gói Đọc", PriceVnd = 49000, AiCredits = 0, IsActive = false
        });

        var forBuyers = await _sut.ListActivePlansAsync(ParentId);

        Assert.DoesNotContain(forBuyers, plan => plan.Id == ReadingPlanId);
        await Assert.ThrowsAsync<BadRequestException>(
            () => _sut.CreateTransactionAsync(ParentId, new CreatePaymentTransactionRequestDto { PlanId = ReadingPlanId }));
    }

    [Fact]
    public async Task UpdatePlanAsync_DoesNotTouchExistingPendingTransaction_NewPurchaseUsesNewValues()
    {
        var before = await _sut.CreateTransactionAsync(ParentId, new CreatePaymentTransactionRequestDto { PlanId = CreativePlanId });

        await _sut.UpdatePlanAsync(AdminId, CreativePlanId, new UpdateSubscriptionPlanRequestDto
        {
            Name = "Gói Sáng tạo", PriceVnd = 120000, AiCredits = 6, IsActive = true
        });
        var after = await _sut.CreateTransactionAsync(OtherParentId, new CreatePaymentTransactionRequestDto { PlanId = CreativePlanId });

        var stored = _store.Items<PaymentTransaction>();
        var oldTransaction = stored.Single(item => item.Id == before.Id);
        var newTransaction = stored.Single(item => item.Id == after.Id);
        Assert.Equal((99000, (int?)4), (oldTransaction.Amount, oldTransaction.AiCredits));
        Assert.Equal((120000, (int?)6), (newTransaction.Amount, newTransaction.AiCredits));
    }

    // ---------- ListMyTransactionsAsync ----------

    [Fact]
    public async Task ListMyTransactionsAsync_ReturnsOnlyOwnNewestFirst()
    {
        var now = DateTime.UtcNow;
        var oldest = SeedTransaction(ReadingPlanId, PaymentStatus.Expired, createdAt: now.AddDays(-30));
        SeedTransaction(CreativePlanId, PaymentStatus.Paid, payerUserId: OtherParentId, createdAt: now.AddDays(-2));
        var newest = SeedTransaction(AddOnPlanId, PaymentStatus.Paid, createdAt: now.AddDays(-1));
        var middle = SeedTransaction(CreativePlanId, PaymentStatus.Paid, createdAt: now.AddDays(-9));

        var result = await _sut.ListMyTransactionsAsync(ParentId, pageIndex: 1, pageSize: 20);

        Assert.Equal([newest.Id, middle.Id, oldest.Id], result.Items.Select(item => item.Id));
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(("Add-on", "AddOn", newest.CreatedAt), (result.Items[0].PlanName, result.Items[0].PlanType, result.Items[0].CreatedAt));
    }

    [Fact]
    public async Task ListMyTransactionsAsync_Pages()
    {
        var now = DateTime.UtcNow;
        for (var day = 1; day <= 5; day++)
        {
            SeedTransaction(ReadingPlanId, PaymentStatus.Expired, createdAt: now.AddDays(-day));
        }

        var result = await _sut.ListMyTransactionsAsync(ParentId, pageIndex: 2, pageSize: 2);

        Assert.Equal((5, 2, 2), (result.TotalCount, result.PageIndex, result.Items.Count));
        Assert.Equal(now.AddDays(-3), result.Items[0].CreatedAt);
    }

    [Fact]
    public async Task ListMyTransactionsAsync_QrOnlyForUnexpiredPending()
    {
        var now = DateTime.UtcNow;
        var live = SeedTransaction(CreativePlanId, expiresAt: now.AddMinutes(5), createdAt: now.AddMinutes(-10));
        var stale = SeedTransaction(ReadingPlanId, expiresAt: now.AddMinutes(-1), createdAt: now.AddMinutes(-16));
        var paid = SeedTransaction(AddOnPlanId, PaymentStatus.Paid, createdAt: now.AddDays(-1));

        var result = await _sut.ListMyTransactionsAsync(ParentId, pageIndex: 1, pageSize: 20);

        var byId = result.Items.ToDictionary(item => item.Id);
        Assert.Equal(("Pending", "https://qr.sepay.vn/img?seed=1"), (byId[live.Id].Status, byId[live.Id].QrCodeUrl));
        Assert.Equal(("Expired", (string?)null), (byId[stale.Id].Status, byId[stale.Id].QrCodeUrl));
        Assert.Equal(("Paid", (string?)null), (byId[paid.Id].Status, byId[paid.Id].QrCodeUrl));
    }
}
