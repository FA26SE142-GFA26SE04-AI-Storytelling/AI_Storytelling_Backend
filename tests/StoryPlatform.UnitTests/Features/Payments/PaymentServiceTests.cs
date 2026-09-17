using System.Linq.Expressions;
using Moq;
using StoryPlatform.Application.Abstractions.Payments;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Notifications.DTOs;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Application.Features.Payments.DTOs;
using StoryPlatform.Application.Features.Payments.Services;
using StoryPlatform.Application.Features.TokenQuota.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Payments;

public class PaymentServiceTests
{
    private readonly Mock<IGenericRepository<SubscriptionPlan>> _planRepository = new();
    private readonly Mock<IGenericRepository<PaymentTransaction>> _transactionRepository = new();
    private readonly Mock<IGenericRepository<UserAccount>> _userRepository = new();
    private readonly Mock<IGenericRepository<OrganizationMembership>> _membershipRepository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IAuditLogWriter> _auditLogWriter = new();
    private readonly Mock<INotificationService> _notificationService = new();
    private readonly Mock<ISePayQrUrlBuilder> _qrUrlBuilder = new();
    private readonly Mock<ISePayWebhookAuthenticator> _webhookAuthenticator = new();
    private readonly Mock<ITokenQuotaService> _tokenQuotaService = new();
    private readonly PaymentService _sut;

    public PaymentServiceTests()
    {
        _unitOfWork.Setup(work => work.Repository<SubscriptionPlan>()).Returns(_planRepository.Object);
        _unitOfWork.Setup(work => work.Repository<PaymentTransaction>()).Returns(_transactionRepository.Object);
        _unitOfWork.Setup(work => work.Repository<UserAccount>()).Returns(_userRepository.Object);
        _unitOfWork.Setup(work => work.Repository<OrganizationMembership>()).Returns(_membershipRepository.Object);

        _notificationService
            .Setup(service => service.CreateAsync(
                It.IsAny<int>(), It.IsAny<NotificationType>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationDto());
        _qrUrlBuilder.Setup(builder => builder.BuildQrCodeUrl(It.IsAny<int>(), It.IsAny<string>()))
            .Returns("https://qr.sepay.vn/img?fake=1");
        _webhookAuthenticator.Setup(auth => auth.IsValid(It.IsAny<string?>())).Returns(true);

        _sut = new PaymentService(
            _unitOfWork.Object, _auditLogWriter.Object, _notificationService.Object,
            _qrUrlBuilder.Object, _webhookAuthenticator.Object, _tokenQuotaService.Object);
    }

    private static SubscriptionPlan MakePlan(int id, ProfileScope scope, int price, bool isActive = true) => new()
    {
        Id = id,
        Name = scope == ProfileScope.Personal ? "Personal" : "Organization",
        ApplicableScope = scope,
        PriceVnd = price,
        QuotaAmount = 50,
        IsActive = isActive
    };

    private static UserAccount MakeUser(int id, UserRole role) => new() { Id = id, Role = role, Email = $"u{id}@x.com" };

    // ---------- ListActivePlansAsync ----------

    [Fact]
    public async Task ListActivePlansAsync_ReturnsOnlyActivePlans()
    {
        var plans = new[] { MakePlan(1, ProfileScope.Personal, 49000), MakePlan(2, ProfileScope.Organization, 99000, isActive: false) };
        _planRepository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<SubscriptionPlan, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<SubscriptionPlan, bool>> predicate, string? _, CancellationToken _) =>
                plans.Where(predicate.Compile()).ToList());

        var result = await _sut.ListActivePlansAsync();

        Assert.Single(result);
        Assert.Equal(1, result[0].Id);
    }

    // ---------- CreateTransactionAsync ----------

    [Fact]
    public async Task CreateTransactionAsync_PlanNotFound_ThrowsNotFound()
    {
        _planRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync((SubscriptionPlan?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.CreateTransactionAsync(
            1, new CreatePaymentTransactionRequestDto { PlanId = 1 }));
    }

    [Fact]
    public async Task CreateTransactionAsync_PlanInactive_ThrowsBadRequest()
    {
        _planRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakePlan(1, ProfileScope.Personal, 49000, isActive: false));

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.CreateTransactionAsync(
            1, new CreatePaymentTransactionRequestDto { PlanId = 1 }));
    }

    [Fact]
    public async Task CreateTransactionAsync_PersonalScope_NonParentPayer_ThrowsForbidden()
    {
        _planRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakePlan(1, ProfileScope.Personal, 49000));
        _userRepository.Setup(repo => repo.GetByIdAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeUser(10, UserRole.Teacher));

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.CreateTransactionAsync(
            10, new CreatePaymentTransactionRequestDto { PlanId = 1 }));
    }

    [Fact]
    public async Task CreateTransactionAsync_PersonalScope_WithOrganizationId_ThrowsBadRequest()
    {
        _planRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakePlan(1, ProfileScope.Personal, 49000));
        _userRepository.Setup(repo => repo.GetByIdAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeUser(10, UserRole.Parent));

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.CreateTransactionAsync(
            10, new CreatePaymentTransactionRequestDto { PlanId = 1, OrganizationId = 5 }));
    }

    [Fact]
    public async Task CreateTransactionAsync_OrganizationScope_MissingOrganizationId_ThrowsBadRequest()
    {
        _planRepository.Setup(repo => repo.GetByIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakePlan(2, ProfileScope.Organization, 99000));

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.CreateTransactionAsync(
            10, new CreatePaymentTransactionRequestDto { PlanId = 2, OrganizationId = null }));
    }

    [Fact]
    public async Task CreateTransactionAsync_OrganizationScope_NotSchoolAdminOfOrg_ThrowsForbidden()
    {
        _planRepository.Setup(repo => repo.GetByIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakePlan(2, ProfileScope.Organization, 99000));
        _membershipRepository.Setup(repo => repo.ExistsAsync(
                It.IsAny<Expression<Func<OrganizationMembership, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.CreateTransactionAsync(
            10, new CreatePaymentTransactionRequestDto { PlanId = 2, OrganizationId = 5 }));
    }

    [Fact]
    public async Task CreateTransactionAsync_DuplicatePending_ThrowsConflict()
    {
        _planRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakePlan(1, ProfileScope.Personal, 49000));
        _userRepository.Setup(repo => repo.GetByIdAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeUser(10, UserRole.Parent));
        _transactionRepository.Setup(repo => repo.ExistsAsync(
                It.IsAny<Expression<Func<PaymentTransaction, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await Assert.ThrowsAsync<ConflictException>(() => _sut.CreateTransactionAsync(
            10, new CreatePaymentTransactionRequestDto { PlanId = 1 }));
    }

    [Fact]
    public async Task CreateTransactionAsync_ValidPersonal_CreatesPendingTransactionWithQr()
    {
        _planRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakePlan(1, ProfileScope.Personal, 49000));
        _userRepository.Setup(repo => repo.GetByIdAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeUser(10, UserRole.Parent));
        _transactionRepository.Setup(repo => repo.ExistsAsync(
                It.IsAny<Expression<Func<PaymentTransaction, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        PaymentTransaction? added = null;
        _transactionRepository.Setup(repo => repo.AddAsync(It.IsAny<PaymentTransaction>(), It.IsAny<CancellationToken>()))
            .Callback<PaymentTransaction, CancellationToken>((entity, _) => added = entity)
            .ReturnsAsync((PaymentTransaction entity, CancellationToken _) => entity);

        var before = DateTime.UtcNow;
        var result = await _sut.CreateTransactionAsync(10, new CreatePaymentTransactionRequestDto { PlanId = 1 });

        Assert.NotNull(added);
        Assert.Equal(1, added!.PlanId);
        Assert.Equal(10, added.PayerUserId);
        Assert.Null(added.OrganizationId);
        Assert.Equal(49000, added.Amount);
        Assert.Equal(PaymentStatus.Pending, added.Status);
        Assert.NotEmpty(added.TransactionCode);
        Assert.True(added.ExpiresAt >= before.AddMinutes(15) && added.ExpiresAt <= DateTime.UtcNow.AddMinutes(15).AddSeconds(5));
        Assert.Equal("https://qr.sepay.vn/img?fake=1", added.QrCodeUrl);
        Assert.Equal("https://qr.sepay.vn/img?fake=1", result.QrCodeUrl);
        Assert.Equal("Pending", result.Status);
    }

    [Fact]
    public async Task CreateTransactionAsync_ValidOrganization_SetsOrganizationId()
    {
        _planRepository.Setup(repo => repo.GetByIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakePlan(2, ProfileScope.Organization, 99000));
        _membershipRepository.Setup(repo => repo.ExistsAsync(
                It.IsAny<Expression<Func<OrganizationMembership, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _transactionRepository.Setup(repo => repo.ExistsAsync(
                It.IsAny<Expression<Func<PaymentTransaction, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        PaymentTransaction? added = null;
        _transactionRepository.Setup(repo => repo.AddAsync(It.IsAny<PaymentTransaction>(), It.IsAny<CancellationToken>()))
            .Callback<PaymentTransaction, CancellationToken>((entity, _) => added = entity)
            .ReturnsAsync((PaymentTransaction entity, CancellationToken _) => entity);

        await _sut.CreateTransactionAsync(10, new CreatePaymentTransactionRequestDto { PlanId = 2, OrganizationId = 5 });

        Assert.Equal(5, added!.OrganizationId);
    }

    // ---------- HandleWebhookAsync ----------

    [Fact]
    public async Task HandleWebhookAsync_InvalidAuth_ThrowsUnauthorized()
    {
        _webhookAuthenticator.Setup(auth => auth.IsValid(It.IsAny<string?>())).Returns(false);

        await Assert.ThrowsAsync<UnauthorizedException>(() => _sut.HandleWebhookAsync(
            "bad", new SePayWebhookPayloadDto { Content = "x", TransferAmount = 1000 }));

        _transactionRepository.Verify(repo => repo.Update(It.IsAny<PaymentTransaction>()), Times.Never);
    }

    private static PaymentTransaction MakeTransaction(
        int id, string code, int amount, PaymentStatus status = PaymentStatus.Pending) => new()
    {
        Id = id,
        PlanId = 1,
        Plan = new SubscriptionPlan { Id = 1, PriceVnd = amount },
        PayerUserId = 10,
        TransactionCode = code,
        Amount = amount,
        Status = status,
        ExpiresAt = DateTime.UtcNow.AddMinutes(10)
    };

    [Fact]
    public async Task HandleWebhookAsync_NoMatchingTransactionCode_NoOp()
    {
        _transactionRepository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<PaymentTransaction, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PaymentTransaction>());

        await _sut.HandleWebhookAsync("valid", new SePayWebhookPayloadDto
        {
            Content = "chuyen khoan noi dung khong khop", TransferAmount = 49000
        });

        _transactionRepository.Verify(repo => repo.Update(It.IsAny<PaymentTransaction>()), Times.Never);
    }

    [Fact]
    public async Task HandleWebhookAsync_AmountMatches_MarksPaidAndNotifies()
    {
        var transaction = MakeTransaction(1, "SEPAYABC123", 49000);
        _transactionRepository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<PaymentTransaction, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { transaction });
        _planRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakePlan(1, ProfileScope.Personal, 49000));

        await _sut.HandleWebhookAsync("valid", new SePayWebhookPayloadDto
        {
            Content = $"chuyen tien {transaction.TransactionCode} thanh toan",
            TransferAmount = 49000,
            ReferenceCode = "FT2600123456"
        });

        Assert.Equal(PaymentStatus.Paid, transaction.Status);
        Assert.NotNull(transaction.PaidAt);
        Assert.Equal("FT2600123456", transaction.SepayTransactionId);
        _notificationService.Verify(service => service.CreateAsync(
            transaction.PayerUserId, NotificationType.PaymentConfirmed, It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _tokenQuotaService.Verify(q => q.CreditAsync(
            ProfileScope.Personal, transaction.PayerUserId, null, 50, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleWebhookAsync_AmountMismatch_SetsMismatchStatusWithoutNotification()
    {
        var transaction = MakeTransaction(1, "SEPAYXYZ999", 49000);
        _transactionRepository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<PaymentTransaction, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { transaction });

        await _sut.HandleWebhookAsync("valid", new SePayWebhookPayloadDto
        {
            Content = $"chuyen tien {transaction.TransactionCode}",
            TransferAmount = 40000
        });

        Assert.Equal(PaymentStatus.MismatchAmount, transaction.Status);
        Assert.Null(transaction.PaidAt);
        _notificationService.Verify(service => service.CreateAsync(
            It.IsAny<int>(), It.IsAny<NotificationType>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleWebhookAsync_TransactionAlreadyPaid_IsIdempotentNoOp()
    {
        var transaction = MakeTransaction(1, "SEPAYDONE1", 49000, PaymentStatus.Paid);
        _transactionRepository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<PaymentTransaction, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { transaction });

        await _sut.HandleWebhookAsync("valid", new SePayWebhookPayloadDto
        {
            Content = $"chuyen tien {transaction.TransactionCode}", TransferAmount = 49000
        });

        _transactionRepository.Verify(repo => repo.Update(It.IsAny<PaymentTransaction>()), Times.Never);
    }

    // ---------- ListMismatchedAsync ----------

    [Fact]
    public async Task ListMismatchedAsync_ReturnsOnlyMismatchedTransactions()
    {
        var mismatched = MakeTransaction(1, "M1", 49000, PaymentStatus.MismatchAmount);
        var paid = MakeTransaction(2, "P1", 49000, PaymentStatus.Paid);
        _transactionRepository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<PaymentTransaction, bool>>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<PaymentTransaction, bool>> predicate, string? _, CancellationToken _) =>
                new[] { mismatched, paid }.Where(predicate.Compile()).ToList());

        var result = await _sut.ListMismatchedAsync();

        Assert.Single(result);
        Assert.Equal(1, result[0].Id);
    }

    // ---------- MarkPaidManuallyAsync ----------

    [Fact]
    public async Task MarkPaidManuallyAsync_NotFound_ThrowsNotFound()
    {
        _transactionRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentTransaction?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.MarkPaidManuallyAsync(99, 1));
    }

    [Fact]
    public async Task MarkPaidManuallyAsync_AlreadyPaid_ThrowsConflict()
    {
        var transaction = MakeTransaction(1, "M2", 49000, PaymentStatus.Paid);
        _transactionRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction);

        await Assert.ThrowsAsync<ConflictException>(() => _sut.MarkPaidManuallyAsync(99, 1));
    }

    [Fact]
    public async Task MarkPaidManuallyAsync_Valid_SetsPaidWritesAuditAndNotifies()
    {
        var transaction = MakeTransaction(1, "M3", 49000, PaymentStatus.MismatchAmount);
        transaction.OrganizationId = 5;
        _transactionRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction);
        _planRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakePlan(1, ProfileScope.Organization, 99000));

        var result = await _sut.MarkPaidManuallyAsync(99, 1);

        Assert.Equal(PaymentStatus.Paid, transaction.Status);
        Assert.NotNull(transaction.PaidAt);
        Assert.Equal("Paid", result.Status);
        _auditLogWriter.Verify(writer => writer.LogAsync(
            99, "PaymentMarkedPaidManually", nameof(PaymentTransaction), 1,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
        _notificationService.Verify(service => service.CreateAsync(
            transaction.PayerUserId, NotificationType.PaymentConfirmed, It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _tokenQuotaService.Verify(q => q.CreditAsync(
            ProfileScope.Organization, transaction.PayerUserId, 5, 50, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleWebhookAsync_AmountMatches_CreditAsyncThrows_StaysPaidAndWritesAuditLog()
    {
        // Regression test: a CreditAsync failure (e.g. BadRequestException on missing
        // OrganizationId, or any transient error) must not be silently lost. The transaction is
        // already committed as Paid before CreditAsync runs (SePay will not retry a transaction it
        // already saw succeed), so the failure must be recorded via audit log instead of thrown.
        var transaction = MakeTransaction(1, "SEPAYFAIL01", 49000);
        _transactionRepository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<PaymentTransaction, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { transaction });
        _planRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakePlan(1, ProfileScope.Personal, 49000));
        _tokenQuotaService
            .Setup(q => q.CreditAsync(
                It.IsAny<ProfileScope>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BadRequestException("OrganizationId là bắt buộc."));

        await _sut.HandleWebhookAsync("valid", new SePayWebhookPayloadDto
        {
            Content = $"chuyen tien {transaction.TransactionCode} thanh toan",
            TransferAmount = 49000,
            ReferenceCode = "FT2600999999"
        });

        Assert.Equal(PaymentStatus.Paid, transaction.Status);
        _notificationService.Verify(service => service.CreateAsync(
            transaction.PayerUserId, NotificationType.PaymentConfirmed, It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _auditLogWriter.Verify(writer => writer.LogAsync(
            null, "PaymentCreditFailed", nameof(PaymentTransaction), transaction.Id,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleWebhookAsync_AmountMatches_PlanNotFound_StaysPaidAndWritesAuditLog()
    {
        // A missing SubscriptionPlan must not silently skip crediting: it is treated the same as
        // a credit failure for audit purposes.
        var transaction = MakeTransaction(1, "SEPAYNOPLAN1", 49000);
        _transactionRepository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<PaymentTransaction, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { transaction });
        _planRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionPlan?)null);

        await _sut.HandleWebhookAsync("valid", new SePayWebhookPayloadDto
        {
            Content = $"chuyen tien {transaction.TransactionCode} thanh toan",
            TransferAmount = 49000
        });

        Assert.Equal(PaymentStatus.Paid, transaction.Status);
        _tokenQuotaService.Verify(q => q.CreditAsync(
            It.IsAny<ProfileScope>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _auditLogWriter.Verify(writer => writer.LogAsync(
            null, "PaymentCreditFailed", nameof(PaymentTransaction), transaction.Id,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MarkPaidManuallyAsync_CreditAsyncThrows_StaysPaidReturnsDtoAndWritesAuditLog()
    {
        // Same "payment confirmed but nothing credited" bug as the webhook path, via the manual
        // mark-paid admin action. The transaction is already Paid and cannot be retried (Paid
        // transactions are refused), so a CreditAsync failure must be recorded via audit log, and
        // MarkPaidManuallyAsync must still return a valid DTO (using the plan that was found).
        var transaction = MakeTransaction(1, "M4", 49000, PaymentStatus.MismatchAmount);
        _transactionRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction);
        _planRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakePlan(1, ProfileScope.Personal, 49000));
        _tokenQuotaService
            .Setup(q => q.CreditAsync(
                It.IsAny<ProfileScope>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BadRequestException("boom"));

        var result = await _sut.MarkPaidManuallyAsync(99, 1);

        Assert.Equal(PaymentStatus.Paid, transaction.Status);
        Assert.Equal("Paid", result.Status);
        Assert.Equal("Personal", result.PlanName);
        _auditLogWriter.Verify(writer => writer.LogAsync(
            99, "PaymentCreditFailed", nameof(PaymentTransaction), transaction.Id,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MarkPaidManuallyAsync_PlanNotFound_StaysPaidReturnsEmptyPlanNameAndWritesAuditLog()
    {
        var transaction = MakeTransaction(1, "M5", 49000, PaymentStatus.Pending);
        _transactionRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction);
        _planRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionPlan?)null);

        var result = await _sut.MarkPaidManuallyAsync(99, 1);

        Assert.Equal(PaymentStatus.Paid, transaction.Status);
        Assert.Equal(string.Empty, result.PlanName);
        _tokenQuotaService.Verify(q => q.CreditAsync(
            It.IsAny<ProfileScope>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _auditLogWriter.Verify(writer => writer.LogAsync(
            99, "PaymentCreditFailed", nameof(PaymentTransaction), transaction.Id,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleWebhookAsync_AmountMismatch_DoesNotCreditQuota()
    {
        var transaction = MakeTransaction(1, "SEPAYXYZ999", 49000);
        _transactionRepository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<PaymentTransaction, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { transaction });

        await _sut.HandleWebhookAsync("valid", new SePayWebhookPayloadDto
        {
            Content = $"chuyen tien {transaction.TransactionCode}", TransferAmount = 40000
        });

        _tokenQuotaService.Verify(q => q.CreditAsync(
            It.IsAny<ProfileScope>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
