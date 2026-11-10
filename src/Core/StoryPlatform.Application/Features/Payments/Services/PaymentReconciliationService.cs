using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Payments.DTOs;
using StoryPlatform.Application.Features.Payments.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Payments.Services;

public class PaymentReconciliationService : IPaymentReconciliationService
{
    // Giao dịch vừa Paid có thể đang được áp gói; chờ qua khoảng này mới coi là ngoại lệ.
    private static readonly TimeSpan DetectionGrace = TimeSpan.FromMinutes(5);
    private const int ReasonMaxLength = 500;
    private const string NotAppliedReason = "NotApplied";

    private readonly IUnitOfWork _unitOfWork;
    private readonly ISubscriptionService _subscriptionService;
    private readonly IAuditLogWriter _auditLogWriter;

    public PaymentReconciliationService(
        IUnitOfWork unitOfWork, ISubscriptionService subscriptionService, IAuditLogWriter auditLogWriter)
    {
        _unitOfWork = unitOfWork;
        _subscriptionService = subscriptionService;
        _auditLogWriter = auditLogWriter;
    }

    public async Task<int> DetectAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        var cutoff = now - DetectionGrace;
        var paid = await _unitOfWork.Repository<PaymentTransaction>().FindAsync(
            item => item.Status == PaymentStatus.Paid && item.PaidAt != null && item.PaidAt <= cutoff,
            cancellationToken: cancellationToken);
        if (paid.Count == 0)
        {
            return 0;
        }

        var exceptionRepo = _unitOfWork.Repository<PaymentReconciliationException>();
        var known = (await exceptionRepo.GetAllAsync(cancellationToken)).Select(item => item.PaymentId).ToHashSet();
        var withSubscription = (await _unitOfWork.Repository<UserSubscription>().GetAllAsync(cancellationToken))
            .Select(item => item.PaymentId).ToHashSet();
        var withLot = (await _unitOfWork.Repository<CreditLot>().FindAsync(
                lot => lot.PaymentId != null, cancellationToken: cancellationToken))
            .Select(lot => lot.PaymentId!.Value).ToHashSet();
        var plans = (await _unitOfWork.Repository<SubscriptionPlan>().GetAllAsync(cancellationToken))
            .ToDictionary(plan => plan.Id);
        var users = _unitOfWork.Repository<UserAccount>();

        var created = 0;
        foreach (var transaction in paid)
        {
            if (known.Contains(transaction.Id) || IsApplied(transaction, plans, withSubscription, withLot))
            {
                continue;
            }

            // Tài khoản đã xoá thì chu kỳ và lô lượt bị huỷ có chủ đích, không phải lỗi áp gói.
            var payer = await users.GetByIdAsync(transaction.PayerUserId, cancellationToken);
            if (payer == null || payer.IsDeleted)
            {
                continue;
            }

            await exceptionRepo.AddAsync(
                new PaymentReconciliationException
                {
                    PaymentId = transaction.Id,
                    Status = ReconciliationStatus.Open,
                    Reason = await FindFailureReasonAsync(transaction.Id, cancellationToken)
                },
                cancellationToken);
            created++;
        }

        if (created > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return created;
    }

    public async Task<PagedResult<ReconciliationExceptionDto>> ListAsync(
        string? status, int pageIndex, int pageSize, CancellationToken cancellationToken = default)
    {
        var wanted = ReconciliationStatus.Open;
        if (!string.IsNullOrWhiteSpace(status)
            && (!Enum.TryParse(status.Trim(), ignoreCase: true, out wanted) || !Enum.IsDefined(wanted)))
        {
            throw new BadRequestException("Trạng thái ngoại lệ phải là 'Open' hoặc 'Resolved'.");
        }

        pageIndex = Math.Max(pageIndex, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var (items, totalCount) = await _unitOfWork.Repository<PaymentReconciliationException>().GetPagedAsync(
            pageIndex, pageSize,
            filter: item => item.Status == wanted,
            orderBy: source => source.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Id),
            cancellationToken: cancellationToken);

        var result = new List<ReconciliationExceptionDto>(items.Count);
        foreach (var item in items)
        {
            result.Add(await MapToDtoAsync(item, cancellationToken));
        }

        return new PagedResult<ReconciliationExceptionDto>(result, totalCount, pageIndex, pageSize);
    }

    public async Task<ReconciliationExceptionDto> ResolveAsync(
        int adminUserId, int exceptionId, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<PaymentReconciliationException>();
        var exception = await repo.GetByIdAsync(exceptionId, cancellationToken)
                        ?? throw new NotFoundException("Ngoại lệ đối soát", exceptionId);
        if (exception.Status == ReconciliationStatus.Resolved)
        {
            throw new ConflictException("Ngoại lệ này đã được xử lý.");
        }

        var transaction = await _unitOfWork.Repository<PaymentTransaction>().GetByIdAsync(exception.PaymentId, cancellationToken)
                          ?? throw new NotFoundException("Giao dịch thanh toán", exception.PaymentId);
        var plan = await _unitOfWork.Repository<SubscriptionPlan>().GetByIdAsync(transaction.PlanId, cancellationToken);
        var now = DateTime.UtcNow;

        try
        {
            if (plan == null)
            {
                throw new InvalidOperationException("SubscriptionPlanNotFound");
            }

            // Cùng bước áp gói với webhook và mark-paid; idempotent theo Id giao dịch nên gói đã được áp
            // từ trước (ví dụ webhook gửi lại thành công) thì không cộng thêm.
            await _subscriptionService.ApplyPaidTransactionAsync(transaction, plan, now, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            exception.Reason = Truncate($"ApplyPlanThrew: {ex.Message}");
            exception.UpdatedAt = now;
            repo.Update(exception);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            throw new ConflictException($"Áp lại gói chưa thành công: {ex.Message}");
        }

        var beforeState = new { status = exception.Status.ToString(), exception.Reason };
        exception.Status = ReconciliationStatus.Resolved;
        exception.ResolvedBy = adminUserId;
        exception.ResolvedAt = now;
        exception.UpdatedAt = now;
        repo.Update(exception);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogWriter.LogAsync(
            adminUserId, "PaymentReconciliationResolved", nameof(PaymentReconciliationException), exception.Id,
            beforeState, new { status = exception.Status.ToString(), paymentId = exception.PaymentId }, cancellationToken);

        return await MapToDtoAsync(exception, cancellationToken);
    }

    /// <summary>
    /// Gói Đọc / Gói Sáng tạo: phải có chu kỳ gắn giao dịch (Gói Đọc có 0 lượt nên không kiểm theo lượt).
    /// Add-on / gói cũ: phải có lô lượt gắn giao dịch, trừ khi giao dịch không có lượt nào để cấp.
    /// </summary>
    private static bool IsApplied(
        PaymentTransaction transaction, Dictionary<int, SubscriptionPlan> plans,
        HashSet<int> withSubscription, HashSet<int> withLot)
    {
        if (!plans.TryGetValue(transaction.PlanId, out var plan))
        {
            return false;
        }

        if (plan.PlanType is PlanType.Reading or PlanType.Creative)
        {
            return withSubscription.Contains(transaction.Id);
        }

        return (transaction.AiCredits ?? plan.AiCredits) <= 0 || withLot.Contains(transaction.Id);
    }

    private async Task<string> FindFailureReasonAsync(int paymentId, CancellationToken cancellationToken)
    {
        var failures = await _unitOfWork.Repository<AuditLog>().FindAsync(
            log => log.Action == "PaymentCreditFailed"
                   && log.EntityType == nameof(PaymentTransaction)
                   && log.EntityId == paymentId,
            cancellationToken: cancellationToken);
        var latest = failures.OrderByDescending(log => log.OccurredAt).ThenByDescending(log => log.Id).FirstOrDefault();
        return string.IsNullOrWhiteSpace(latest?.AfterState) ? NotAppliedReason : Truncate(latest.AfterState);
    }

    private async Task<ReconciliationExceptionDto> MapToDtoAsync(
        PaymentReconciliationException exception, CancellationToken cancellationToken)
    {
        var users = _unitOfWork.Repository<UserAccount>();
        var transaction = await _unitOfWork.Repository<PaymentTransaction>().GetByIdAsync(exception.PaymentId, cancellationToken);
        var plan = transaction == null
            ? null
            : await _unitOfWork.Repository<SubscriptionPlan>().GetByIdAsync(transaction.PlanId, cancellationToken);
        var payer = transaction == null ? null : await users.GetByIdAsync(transaction.PayerUserId, cancellationToken);
        var resolver = exception.ResolvedBy.HasValue
            ? await users.GetByIdAsync(exception.ResolvedBy.Value, cancellationToken)
            : null;

        return new ReconciliationExceptionDto
        {
            Id = exception.Id,
            Status = exception.Status.ToString(),
            Reason = exception.Reason,
            CreatedAt = exception.CreatedAt,
            ResolvedAt = exception.ResolvedAt,
            ResolvedByName = resolver == null
                ? null
                : string.IsNullOrWhiteSpace(resolver.FullName) ? resolver.Username : resolver.FullName,
            Transaction = transaction == null
                ? new AdminPaymentTransactionDto { Id = exception.PaymentId }
                : PaymentService.MapToAdminDto(transaction, plan, payer)
        };
    }

    private static string Truncate(string value) =>
        value.Length <= ReasonMaxLength ? value : value[..ReasonMaxLength];
}
