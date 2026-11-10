using System.Text.Json;
using StoryPlatform.Application.Abstractions.Payments;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Application.Features.Payments.DTOs;
using StoryPlatform.Application.Features.Payments.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Payments.Services;

public class PaymentService : IPaymentService
{
    private static readonly TimeSpan TransactionTtl = TimeSpan.FromMinutes(15);

    private const string AddOnLockedReason = "Hãy đăng ký Gói Đọc hoặc Gói Sáng tạo trước khi mua Add-on.";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly INotificationService _notificationService;
    private readonly ISePayQrUrlBuilder _qrUrlBuilder;
    private readonly ISePayWebhookAuthenticator _webhookAuthenticator;
    private readonly ISubscriptionService _subscriptionService;

    public PaymentService(
        IUnitOfWork unitOfWork,
        IAuditLogWriter auditLogWriter,
        INotificationService notificationService,
        ISePayQrUrlBuilder qrUrlBuilder,
        ISePayWebhookAuthenticator webhookAuthenticator,
        ISubscriptionService subscriptionService)
    {
        _unitOfWork = unitOfWork;
        _auditLogWriter = auditLogWriter;
        _notificationService = notificationService;
        _qrUrlBuilder = qrUrlBuilder;
        _webhookAuthenticator = webhookAuthenticator;
        _subscriptionService = subscriptionService;
    }

    public async Task<List<SubscriptionPlanDto>> ListActivePlansAsync(
        int userId, CancellationToken cancellationToken = default)
    {
        var plans = await _unitOfWork.Repository<SubscriptionPlan>().FindAsync(
            item => item.IsActive && item.PlanType != PlanType.Legacy, cancellationToken: cancellationToken);
        var hasActivePlan =
            await _subscriptionService.GetActiveAsync(userId, DateTime.UtcNow, cancellationToken) != null;

        return plans
            .OrderBy(plan => plan.Id)
            .Select(plan =>
            {
                // Add-on chỉ mua được khi đang có Gói Đọc hoặc Gói Sáng tạo còn hiệu lực.
                var locked = plan.PlanType == PlanType.AddOn && !hasActivePlan;
                return new SubscriptionPlanDto
                {
                    Id = plan.Id,
                    Name = plan.Name,
                    PlanType = plan.PlanType.ToString(),
                    PriceVnd = plan.PriceVnd,
                    AiCredits = plan.AiCredits,
                    IsPurchasable = !locked,
                    LockedReason = locked ? AddOnLockedReason : null
                };
            })
            .ToList();
    }

    public async Task<PaymentTransactionDto> CreateTransactionAsync(
        int payerUserId, CreatePaymentTransactionRequestDto request, CancellationToken cancellationToken = default)
    {
        var plan = await _unitOfWork.Repository<SubscriptionPlan>().GetByIdAsync(request.PlanId, cancellationToken)
                   ?? throw new NotFoundException("Gói Subscription", request.PlanId);
        if (!plan.IsActive || plan.PlanType == PlanType.Legacy)
        {
            throw new BadRequestException("Gói này hiện không còn hoạt động.");
        }

        var payer = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(payerUserId, cancellationToken)
                    ?? throw new ForbiddenException();
        if (payer.Role != UserRole.User)
        {
            throw new ForbiddenException("Chỉ tài khoản phụ huynh mới được mua gói này.");
        }

        var now = DateTime.UtcNow;
        var isAddOn = plan.PlanType == PlanType.AddOn;
        if (isAddOn && await _subscriptionService.GetActiveAsync(payerUserId, now, cancellationToken) == null)
        {
            throw new ConflictException(AddOnLockedReason);
        }

        // Mỗi tài khoản chỉ có 1 giao dịch gói (Gói Đọc / Gói Sáng tạo) chờ thanh toán; Add-on tính riêng.
        // Giao dịch Pending đã quá hạn mà worker chưa quét không được chặn giao dịch mới.
        var planTypes = await GetPlanTypesAsync(cancellationToken);
        var livePending = await _unitOfWork.Repository<PaymentTransaction>().FindAsync(
            item => item.PayerUserId == payerUserId
                    && item.Status == PaymentStatus.Pending
                    && item.ExpiresAt > now,
            cancellationToken: cancellationToken);
        var duplicatePending = livePending.Any(item =>
            planTypes.TryGetValue(item.PlanId, out var pendingType) && (pendingType == PlanType.AddOn) == isAddOn);
        if (duplicatePending)
        {
            throw new ConflictException(isAddOn
                ? "Bạn đang có một giao dịch Add-on chờ thanh toán. Hãy hoàn tất hoặc chờ giao dịch đó hết hạn."
                : "Bạn đang có một giao dịch gói chờ thanh toán. Hãy hoàn tất hoặc chờ giao dịch đó hết hạn.");
        }

        var transactionCode = GenerateTransactionCode();
        var qrCodeUrl = _qrUrlBuilder.BuildQrCodeUrl(plan.PriceVnd, transactionCode);
        var transaction = new PaymentTransaction
        {
            PlanId = plan.Id,
            PayerUserId = payerUserId,
            TransactionCode = transactionCode,
            Amount = plan.PriceVnd,
            AiCredits = plan.AiCredits,
            Status = PaymentStatus.Pending,
            QrCodeUrl = qrCodeUrl,
            ExpiresAt = now.Add(TransactionTtl)
        };

        await _unitOfWork.Repository<PaymentTransaction>().AddAsync(transaction, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToDto(transaction, plan, now);
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

            await ApplyPlanSafelyAsync(transaction, actorUserId: null, cancellationToken);
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
            item => item.Status == PaymentStatus.MismatchAmount, cancellationToken: cancellationToken);
        var plans = await GetPlansAsync(cancellationToken);
        var now = DateTime.UtcNow;
        return mismatched.Select(item => MapToDto(item, plans.GetValueOrDefault(item.PlanId), now)).ToList();
    }

    public async Task<PagedResult<PaymentTransactionDto>> ListMyTransactionsAsync(
        int userId, int pageIndex, int pageSize, CancellationToken cancellationToken = default)
    {
        var (items, totalCount) = await _unitOfWork.Repository<PaymentTransaction>().GetPagedAsync(
            pageIndex, pageSize,
            filter: item => item.PayerUserId == userId,
            orderBy: query => query.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Id),
            cancellationToken: cancellationToken);
        var plans = await GetPlansAsync(cancellationToken);
        var now = DateTime.UtcNow;

        return new PagedResult<PaymentTransactionDto>(
            items.Select(item => MapToDto(item, plans.GetValueOrDefault(item.PlanId), now)).ToList(),
            totalCount, pageIndex, pageSize);
    }

    public async Task<PagedResult<AdminPaymentTransactionDto>> ListAllTransactionsAsync(
        AdminTransactionQueryDto query, CancellationToken cancellationToken = default)
    {
        PaymentStatus? status = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (!Enum.TryParse<PaymentStatus>(query.Status.Trim(), ignoreCase: true, out var parsed)
                || !Enum.IsDefined(parsed))
            {
                throw new BadRequestException("Trạng thái giao dịch không hợp lệ.");
            }

            status = parsed;
        }

        // Khớp một phần, không phân biệt hoa thường, theo nghĩa đen (Contains không coi % và _ là ký tự đại diện).
        var term = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim().ToLower();
        var pageIndex = Math.Max(query.PageIndex, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var (items, totalCount) = await _unitOfWork.Repository<PaymentTransaction>().GetPagedAsync(
            pageIndex, pageSize,
            filter: item =>
                (status == null || item.Status == status)
                && (term == null
                    || item.TransactionCode.ToLower().Contains(term)
                    || item.PayerUser!.Email.ToLower().Contains(term)
                    || item.PayerUser!.Username.ToLower().Contains(term)),
            orderBy: source => source.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Id),
            includeProperties: "PayerUser",
            cancellationToken: cancellationToken);
        var plans = await GetPlansAsync(cancellationToken);

        return new PagedResult<AdminPaymentTransactionDto>(
            items.Select(item => MapToAdminDto(item, plans.GetValueOrDefault(item.PlanId), item.PayerUser)).ToList(),
            totalCount, pageIndex, pageSize);
    }

    public async Task<List<AdminSubscriptionPlanDto>> ListAllPlansAsync(CancellationToken cancellationToken = default) =>
        (await _unitOfWork.Repository<SubscriptionPlan>().GetAllAsync(cancellationToken))
        .OrderBy(plan => plan.Id)
        .Select(MapToAdminDto)
        .ToList();

    public async Task<AdminSubscriptionPlanDto> UpdatePlanAsync(
        int adminUserId, int planId, UpdateSubscriptionPlanRequestDto request, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<SubscriptionPlan>();
        var plan = await repo.FirstOrDefaultAsync(item => item.Id == planId, cancellationToken: cancellationToken)
                   ?? throw new NotFoundException("Gói Subscription", planId);

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0 || name.Length > 150)
        {
            throw new BadRequestException("Tên gói phải có từ 1 đến 150 ký tự.");
        }

        if (request.PriceVnd <= 0)
        {
            throw new BadRequestException("Giá gói phải lớn hơn 0.");
        }

        if (request.AiCredits < 0)
        {
            throw new BadRequestException("Số lượt AI không được âm.");
        }

        if (plan.PlanType == PlanType.Legacy && request.IsActive)
        {
            throw new BadRequestException("Gói Personal cũ đã ngừng bán và không thể mở bán lại.");
        }

        // Giao dịch đã tạo giữ Amount và AiCredits chốt lúc tạo, nên thay đổi chỉ áp dụng cho giao dịch tạo sau đó.
        var beforeState = new { plan.Name, plan.PriceVnd, plan.AiCredits, plan.IsActive };
        plan.Name = name;
        plan.PriceVnd = request.PriceVnd;
        plan.AiCredits = request.AiCredits;
        plan.IsActive = request.IsActive;
        plan.UpdatedAt = DateTime.UtcNow;
        repo.Update(plan);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogWriter.LogAsync(
            adminUserId, "SubscriptionPlanUpdated", nameof(SubscriptionPlan), plan.Id,
            beforeState, new { plan.Name, plan.PriceVnd, plan.AiCredits, plan.IsActive }, cancellationToken);

        return MapToAdminDto(plan);
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

        var plan = await ApplyPlanSafelyAsync(transaction, actorUserId: adminUserId, cancellationToken);

        return MapToDto(transaction, plan, DateTime.UtcNow);
    }

    /// <summary>
    /// Fetches the transaction's plan and applies it (subscription cycle and/or AI credits, by plan type),
    /// with the transaction already committed as Paid. A missing plan or a failure from
    /// <see cref="ISubscriptionService.ApplyPaidTransactionAsync"/> (e.g. transient error) is recorded as an
    /// audit log entry for an operator to act on rather than propagated — SePay will not retry a transaction
    /// it already saw succeed, and a manual mark-paid refuses an already-Paid transaction, so failing this
    /// step must never roll back or fail the surrounding payment-confirmation flow.
    /// </summary>
    private async Task<SubscriptionPlan?> ApplyPlanSafelyAsync(
        PaymentTransaction transaction, int? actorUserId, CancellationToken cancellationToken)
    {
        var plan = await _unitOfWork.Repository<SubscriptionPlan>().FirstOrDefaultAsync(
            item => item.Id == transaction.PlanId, cancellationToken: cancellationToken);
        if (plan == null)
        {
            await _auditLogWriter.LogAsync(
                actorUserId, "PaymentCreditFailed", nameof(PaymentTransaction), transaction.Id,
                null, new { reason = "SubscriptionPlanNotFound", planId = transaction.PlanId }, cancellationToken);
            return null;
        }

        try
        {
            await _subscriptionService.ApplyPaidTransactionAsync(transaction, plan, DateTime.UtcNow, cancellationToken);
        }
        catch (Exception ex)
        {
            await _auditLogWriter.LogAsync(
                actorUserId, "PaymentCreditFailed", nameof(PaymentTransaction), transaction.Id,
                null,
                new
                {
                    reason = "ApplyPlanThrew",
                    planId = plan.Id,
                    planType = plan.PlanType.ToString(),
                    aiCredits = plan.AiCredits,
                    error = ex.Message
                },
                cancellationToken);
        }

        return plan;
    }

    private async Task<Dictionary<int, SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken) =>
        (await _unitOfWork.Repository<SubscriptionPlan>().GetAllAsync(cancellationToken))
        .ToDictionary(plan => plan.Id);

    private async Task<Dictionary<int, PlanType>> GetPlanTypesAsync(CancellationToken cancellationToken) =>
        (await GetPlansAsync(cancellationToken)).ToDictionary(pair => pair.Key, pair => pair.Value.PlanType);

    private static AdminSubscriptionPlanDto MapToAdminDto(SubscriptionPlan plan) => new()
    {
        Id = plan.Id,
        Name = plan.Name,
        PlanType = plan.PlanType.ToString(),
        PriceVnd = plan.PriceVnd,
        AiCredits = plan.AiCredits,
        IsActive = plan.IsActive
    };

    /// <summary>
    /// Dạng Admin của một giao dịch: giữ đúng trạng thái và QR đang lưu (không tự đổi Pending quá hạn thành
    /// Expired) để Admin thấy trạng thái thật trước khi xác nhận tay, kèm thông tin người mua.
    /// </summary>
    internal static AdminPaymentTransactionDto MapToAdminDto(
        PaymentTransaction transaction, SubscriptionPlan? plan, UserAccount? payer) => new()
    {
        Id = transaction.Id,
        PlanId = transaction.PlanId,
        PlanName = plan?.Name ?? string.Empty,
        PlanType = plan?.PlanType.ToString() ?? string.Empty,
        Amount = transaction.Amount,
        TransactionCode = transaction.TransactionCode,
        QrCodeUrl = transaction.QrCodeUrl,
        Status = transaction.Status.ToString(),
        CreatedAt = transaction.CreatedAt,
        ExpiresAt = transaction.ExpiresAt,
        PaidAt = transaction.PaidAt,
        SepayTransactionId = transaction.SepayTransactionId,
        PayerUserId = transaction.PayerUserId,
        PayerEmail = payer?.Email ?? string.Empty,
        PayerName = string.IsNullOrWhiteSpace(payer?.FullName) ? payer?.Username ?? string.Empty : payer.FullName
    };

    private static string GenerateTransactionCode() =>
        "SEPAY" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();

    private static PaymentTransactionDto MapToDto(PaymentTransaction transaction, SubscriptionPlan? plan, DateTime now)
    {
        // Pending đã quá hạn mà worker chưa quét được hiển thị là Expired; QR chỉ còn ý nghĩa khi Pending còn hạn.
        var isPending = transaction.Status == PaymentStatus.Pending;
        var isLive = isPending && transaction.ExpiresAt > now;
        var status = isPending && !isLive ? PaymentStatus.Expired : transaction.Status;

        return new PaymentTransactionDto
        {
            Id = transaction.Id,
            PlanId = transaction.PlanId,
            PlanName = plan?.Name ?? string.Empty,
            PlanType = plan?.PlanType.ToString() ?? string.Empty,
            Amount = transaction.Amount,
            TransactionCode = transaction.TransactionCode,
            QrCodeUrl = isLive ? transaction.QrCodeUrl : null,
            Status = status.ToString(),
            CreatedAt = transaction.CreatedAt,
            ExpiresAt = transaction.ExpiresAt,
            PaidAt = transaction.PaidAt,
            SepayTransactionId = transaction.SepayTransactionId
        };
    }
}
