using System.Text.Json;
using StoryPlatform.Application.Abstractions.Payments;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Application.Features.Payments.DTOs;
using StoryPlatform.Application.Features.Payments.Interfaces;
using StoryPlatform.Application.Features.TokenQuota.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Payments.Services;

public class PaymentService : IPaymentService
{
    private static readonly TimeSpan TransactionTtl = TimeSpan.FromMinutes(15);

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly INotificationService _notificationService;
    private readonly ISePayQrUrlBuilder _qrUrlBuilder;
    private readonly ISePayWebhookAuthenticator _webhookAuthenticator;
    private readonly ITokenQuotaService _tokenQuotaService;

    public PaymentService(
        IUnitOfWork unitOfWork,
        IAuditLogWriter auditLogWriter,
        INotificationService notificationService,
        ISePayQrUrlBuilder qrUrlBuilder,
        ISePayWebhookAuthenticator webhookAuthenticator,
        ITokenQuotaService tokenQuotaService)
    {
        _unitOfWork = unitOfWork;
        _auditLogWriter = auditLogWriter;
        _notificationService = notificationService;
        _qrUrlBuilder = qrUrlBuilder;
        _webhookAuthenticator = webhookAuthenticator;
        _tokenQuotaService = tokenQuotaService;
    }

    public async Task<List<SubscriptionPlanDto>> ListActivePlansAsync(CancellationToken cancellationToken = default)
    {
        var plans = await _unitOfWork.Repository<SubscriptionPlan>().FindAsync(
            item => item.IsActive, cancellationToken: cancellationToken);
        return plans.Select(MapToDto).ToList();
    }

    public async Task<PaymentTransactionDto> CreateTransactionAsync(
        int payerUserId, CreatePaymentTransactionRequestDto request, CancellationToken cancellationToken = default)
    {
        var plan = await _unitOfWork.Repository<SubscriptionPlan>().GetByIdAsync(request.PlanId, cancellationToken)
                   ?? throw new NotFoundException("Gói Subscription", request.PlanId);
        if (!plan.IsActive)
        {
            throw new BadRequestException("Gói này hiện không còn hoạt động.");
        }

        if (plan.ApplicableScope == ProfileScope.Personal)
        {
            if (request.OrganizationId.HasValue)
            {
                throw new BadRequestException("OrganizationId không áp dụng cho gói Personal.");
            }

            var payer = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(payerUserId, cancellationToken)
                        ?? throw new ForbiddenException();
            if (payer.Role != UserRole.Parent)
            {
                throw new ForbiddenException("Chỉ Parent mới được mua gói Personal.");
            }
        }
        else
        {
            if (!request.OrganizationId.HasValue)
            {
                throw new BadRequestException("OrganizationId là bắt buộc cho gói Organization.");
            }

            var isSchoolAdmin = await _unitOfWork.Repository<OrganizationMembership>().ExistsAsync(
                item => item.UserId == payerUserId
                        && item.OrganizationId == request.OrganizationId.Value
                        && item.Status == MembershipStatus.Active
                        && item.OrgRole == OrgRole.SchoolAdmin,
                cancellationToken);
            if (!isSchoolAdmin)
            {
                throw new ForbiddenException("Chỉ School Admin đang active của Organization này mới được mua gói Organization.");
            }
        }

        var duplicatePending = await _unitOfWork.Repository<PaymentTransaction>().ExistsAsync(
            item => item.PayerUserId == payerUserId
                    && item.PlanId == request.PlanId
                    && item.Status == PaymentStatus.Pending,
            cancellationToken);
        if (duplicatePending)
        {
            throw new ConflictException("Đã có một giao dịch Pending cho gói này, vui lòng hoàn tất hoặc chờ hết hạn.");
        }

        var transactionCode = GenerateTransactionCode();
        var qrCodeUrl = _qrUrlBuilder.BuildQrCodeUrl(plan.PriceVnd, transactionCode);
        var transaction = new PaymentTransaction
        {
            PlanId = plan.Id,
            PayerUserId = payerUserId,
            OrganizationId = plan.ApplicableScope == ProfileScope.Organization ? request.OrganizationId : null,
            TransactionCode = transactionCode,
            Amount = plan.PriceVnd,
            Status = PaymentStatus.Pending,
            QrCodeUrl = qrCodeUrl,
            ExpiresAt = DateTime.UtcNow.Add(TransactionTtl)
        };

        await _unitOfWork.Repository<PaymentTransaction>().AddAsync(transaction, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToDto(transaction, plan.Name);
    }

    public async Task HandleWebhookAsync(
        string? authorizationHeaderValue, SePayWebhookPayloadDto payload, CancellationToken cancellationToken = default)
    {
        if (!_webhookAuthenticator.IsValid(authorizationHeaderValue))
        {
            throw new UnauthorizedException("Webhook không hợp lệ.");
        }

        var pendingTransactions = await _unitOfWork.Repository<PaymentTransaction>().FindAsync(
            item => item.Status == PaymentStatus.Pending, cancellationToken: cancellationToken);

        var content = payload.Content ?? string.Empty;
        var transaction = pendingTransactions.FirstOrDefault(
            item => content.Contains(item.TransactionCode, StringComparison.OrdinalIgnoreCase));
        if (transaction is null || transaction.Status != PaymentStatus.Pending)
        {
            return;
        }

        var beforeState = new { status = transaction.Status.ToString() };
        if (transaction.Amount == payload.TransferAmount)
        {
            transaction.Status = PaymentStatus.Paid;
            transaction.PaidAt = DateTime.UtcNow;
            transaction.SepayTransactionId = payload.ReferenceCode;
            _unitOfWork.Repository<PaymentTransaction>().Update(transaction);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await _auditLogWriter.LogAsync(
                null, "PaymentPaidViaWebhook", nameof(PaymentTransaction), transaction.Id,
                beforeState, new { status = transaction.Status.ToString() }, cancellationToken);
            await _notificationService.CreateAsync(
                transaction.PayerUserId, NotificationType.PaymentConfirmed,
                JsonSerializer.Serialize(new { transactionId = transaction.Id }), cancellationToken);

            var plan = await _unitOfWork.Repository<SubscriptionPlan>().GetByIdAsync(transaction.PlanId, cancellationToken);
            if (plan != null)
            {
                await _tokenQuotaService.CreditAsync(
                    plan.ApplicableScope, transaction.PayerUserId, transaction.OrganizationId, plan.QuotaAmount,
                    cancellationToken);
            }
        }
        else
        {
            transaction.Status = PaymentStatus.MismatchAmount;
            _unitOfWork.Repository<PaymentTransaction>().Update(transaction);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await _auditLogWriter.LogAsync(
                null, "PaymentMismatchAmount", nameof(PaymentTransaction), transaction.Id,
                beforeState, new { status = transaction.Status.ToString(), receivedAmount = payload.TransferAmount },
                cancellationToken);
        }
    }

    public async Task<List<PaymentTransactionDto>> ListMismatchedAsync(CancellationToken cancellationToken = default)
    {
        var mismatched = await _unitOfWork.Repository<PaymentTransaction>().FindAsync(
            item => item.Status == PaymentStatus.MismatchAmount,
            includeProperties: "Plan", cancellationToken: cancellationToken);
        return mismatched.Select(item => MapToDto(item, item.Plan?.Name ?? string.Empty)).ToList();
    }

    public async Task<PaymentTransactionDto> MarkPaidManuallyAsync(
        int adminUserId, int transactionId, CancellationToken cancellationToken = default)
    {
        var transaction = await _unitOfWork.Repository<PaymentTransaction>().GetByIdAsync(transactionId, cancellationToken)
                           ?? throw new NotFoundException("Giao dịch thanh toán", transactionId);
        if (transaction.Status == PaymentStatus.Paid)
        {
            throw new ConflictException("Giao dịch đã ở trạng thái Paid.");
        }

        var beforeState = new { status = transaction.Status.ToString() };
        transaction.Status = PaymentStatus.Paid;
        transaction.PaidAt = DateTime.UtcNow;
        _unitOfWork.Repository<PaymentTransaction>().Update(transaction);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogWriter.LogAsync(
            adminUserId, "PaymentMarkedPaidManually", nameof(PaymentTransaction), transaction.Id,
            beforeState, new { status = transaction.Status.ToString() }, cancellationToken);
        await _notificationService.CreateAsync(
            transaction.PayerUserId, NotificationType.PaymentConfirmed,
            JsonSerializer.Serialize(new { transactionId = transaction.Id }), cancellationToken);

        var plan = await _unitOfWork.Repository<SubscriptionPlan>().GetByIdAsync(transaction.PlanId, cancellationToken);
        if (plan != null)
        {
            await _tokenQuotaService.CreditAsync(
                plan.ApplicableScope, transaction.PayerUserId, transaction.OrganizationId, plan.QuotaAmount,
                cancellationToken);
        }

        return MapToDto(transaction, plan?.Name ?? string.Empty);
    }

    private static string GenerateTransactionCode() =>
        "SEPAY" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();

    private static SubscriptionPlanDto MapToDto(SubscriptionPlan plan) => new()
    {
        Id = plan.Id,
        Name = plan.Name,
        ApplicableScope = plan.ApplicableScope.ToString().ToLowerInvariant(),
        PriceVnd = plan.PriceVnd,
        QuotaAmount = plan.QuotaAmount
    };

    private static PaymentTransactionDto MapToDto(PaymentTransaction transaction, string planName) => new()
    {
        Id = transaction.Id,
        PlanId = transaction.PlanId,
        PlanName = planName,
        Amount = transaction.Amount,
        TransactionCode = transaction.TransactionCode,
        QrCodeUrl = transaction.QrCodeUrl,
        Status = transaction.Status.ToString(),
        ExpiresAt = transaction.ExpiresAt,
        PaidAt = transaction.PaidAt,
        SepayTransactionId = transaction.SepayTransactionId
    };
}
