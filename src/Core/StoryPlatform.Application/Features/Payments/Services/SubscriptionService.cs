using System.Text.Json;
using Microsoft.Extensions.Logging;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Application.Features.Payments.Interfaces;
using StoryPlatform.Application.Features.TokenQuota.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Payments.Services;

public class SubscriptionService : ISubscriptionService
{
    private static readonly TimeSpan RenewalReminderWindow = TimeSpan.FromDays(3);

    private readonly IUnitOfWork _unitOfWork;
    private readonly ITokenQuotaService _tokenQuotaService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        IUnitOfWork unitOfWork,
        ITokenQuotaService tokenQuotaService,
        INotificationService notificationService,
        ILogger<SubscriptionService> logger)
    {
        _unitOfWork = unitOfWork;
        _tokenQuotaService = tokenQuotaService;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<UserSubscription?> GetActiveAsync(
        int userId, DateTime now, CancellationToken cancellationToken = default) =>
        await _unitOfWork.Repository<UserSubscription>().FirstOrDefaultAsync(
            item => item.UserId == userId && item.Status == SubscriptionStatus.Active && item.EndAt > now,
            cancellationToken: cancellationToken);

    public async Task ApplyPaidTransactionAsync(
        PaymentTransaction transaction, SubscriptionPlan plan, DateTime now, CancellationToken cancellationToken = default)
    {
        var userId = transaction.PayerUserId;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            // Khoá theo tài khoản: webhook và mark-paid cùng đến cho một giao dịch chỉ áp gói một lần.
            await _unitOfWork.AcquireTransactionLockAsync(userId, cancellationToken);

            if (!await IsAlreadyAppliedAsync(transaction.Id, cancellationToken))
            {
                await AdvanceCyclesAsync(userId, now, cancellationToken);

                var paidAt = transaction.PaidAt ?? now;
                switch (plan.PlanType)
                {
                    case PlanType.Reading:
                    case PlanType.Creative:
                        await ApplyMonthlyPlanAsync(transaction, plan, paidAt, now, cancellationToken);
                        break;
                    case PlanType.AddOn:
                        // Add-on hết hạn cùng chu kỳ hiện tại; gói đã hết lúc tiền về thì dùng được 1 tháng từ lúc trả.
                        var active = await GetActiveAsync(userId, now, cancellationToken);
                        await _tokenQuotaService.GrantAsync(
                            userId, CreditSource.AddOn, CreditsFor(transaction, plan), active?.EndAt ?? paidAt.AddMonths(1),
                            transaction.Id, cancellationToken: cancellationToken);
                        break;
                    case PlanType.CreatorPack:
                        // Gói Creator (2f): cộng lượt AI và lượt gửi duyệt vào quota Creator, không hết hạn.
                        await _tokenQuotaService.GrantAsync(
                            userId, CreditSource.CreatorPack, CreditsFor(transaction, plan), expiresAt: null, transaction.Id,
                            CreditType.AI, cancellationToken);
                        await _tokenQuotaService.GrantAsync(
                            userId, CreditSource.CreatorPack, transaction.SubmitCredits ?? plan.SubmitCredits, expiresAt: null,
                            transaction.Id, CreditType.Submit, cancellationToken);
                        break;
                    default:
                        await _tokenQuotaService.GrantAsync(
                            userId, CreditSource.Legacy, CreditsFor(transaction, plan), expiresAt: null, transaction.Id,
                            cancellationToken: cancellationToken);
                        break;
                }

                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _unitOfWork.DiscardTrackedChanges();
            throw;
        }
    }

    public async Task AdvanceCyclesAsync(int userId, DateTime now, CancellationToken cancellationToken = default)
    {
        var repo = _unitOfWork.Repository<UserSubscription>();
        var open = (await repo.FindAsync(
                item => item.UserId == userId
                        && (item.Status == SubscriptionStatus.Active || item.Status == SubscriptionStatus.Scheduled),
                cancellationToken: cancellationToken))
            .OrderBy(item => item.StartAt)
            .ToList();

        var changed = false;
        while (true)
        {
            var active = open.FirstOrDefault(item => item.Status == SubscriptionStatus.Active);
            if (active != null)
            {
                if (active.EndAt > now)
                {
                    break;
                }

                active.Status = SubscriptionStatus.Expired;
                active.UpdatedAt = now;
                repo.Update(active);
                changed = true;
                continue;
            }

            var next = open.FirstOrDefault(item => item.Status == SubscriptionStatus.Scheduled && item.StartAt <= now);
            if (next == null)
            {
                break;
            }

            next.Status = SubscriptionStatus.Active;
            next.UpdatedAt = now;
            repo.Update(next);
            changed = true;

            // Chu kỳ đã trôi qua hết (worker ngừng lâu) thì không cấp lượt: vòng lặp kế tiếp sẽ cho nó hết hạn.
            if (next.EndAt > now)
            {
                // Số lượt đã chốt trên giao dịch của chu kỳ này; giao dịch cũ chưa chốt thì theo gói.
                var payment = await _unitOfWork.Repository<PaymentTransaction>().GetByIdAsync(next.PaymentId, cancellationToken);
                var plan = await _unitOfWork.Repository<SubscriptionPlan>().GetByIdAsync(next.PlanId, cancellationToken);
                await _tokenQuotaService.GrantAsync(
                    userId, CreditSource.Cycle, payment?.AiCredits ?? plan?.AiCredits ?? 0, next.EndAt, next.PaymentId,
                    cancellationToken: cancellationToken);
            }
        }

        if (changed)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<int> SweepAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        var due = await _unitOfWork.Repository<UserSubscription>().FindAsync(
            item => (item.Status == SubscriptionStatus.Active && item.EndAt <= now)
                    || (item.Status == SubscriptionStatus.Scheduled && item.StartAt <= now),
            cancellationToken: cancellationToken);

        var processed = 0;
        foreach (var userId in due.Select(item => item.UserId).Distinct())
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            try
            {
                await _unitOfWork.AcquireTransactionLockAsync(userId, cancellationToken);
                await AdvanceCyclesAsync(userId, now, cancellationToken);
                await _unitOfWork.CommitTransactionAsync(cancellationToken);
                processed++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Lỗi ở một tài khoản không chặn các tài khoản khác; lần quét sau sẽ thử lại.
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                _unitOfWork.DiscardTrackedChanges();
                _logger.LogError(exception, "Subscription cycle advance failed for user {UserId}.", userId);
            }
        }

        await SendRenewalRemindersAsync(now, cancellationToken);
        return processed;
    }

    private async Task<bool> IsAlreadyAppliedAsync(int paymentId, CancellationToken cancellationToken) =>
        await _unitOfWork.Repository<UserSubscription>().ExistsAsync(item => item.PaymentId == paymentId, cancellationToken)
        || await _unitOfWork.Repository<CreditLot>().ExistsAsync(lot => lot.PaymentId == paymentId, cancellationToken);

    private async Task ApplyMonthlyPlanAsync(
        PaymentTransaction transaction, SubscriptionPlan plan, DateTime paidAt, DateTime now,
        CancellationToken cancellationToken)
    {
        var userId = transaction.PayerUserId;
        var repo = _unitOfWork.Repository<UserSubscription>();
        var current = await GetActiveAsync(userId, now, cancellationToken);
        var scheduled = (await repo.FindAsync(
                item => item.UserId == userId && item.Status == SubscriptionStatus.Scheduled,
                cancellationToken: cancellationToken))
            .OrderBy(item => item.StartAt)
            .ToList();

        var currentPlan = current == null
            ? null
            : await _unitOfWork.Repository<SubscriptionPlan>().GetByIdAsync(current.PlanId, cancellationToken);
        var isUpgrade = currentPlan?.PlanType == PlanType.Reading && plan.PlanType == PlanType.Creative;

        if (current != null && !isUpgrade)
        {
            // Gia hạn sớm hoặc hạ gói: nối ngay sau dòng cuối chuỗi, lượt cấp khi chu kỳ đó bắt đầu.
            var chainEnd = scheduled.Select(item => item.EndAt).Append(current.EndAt).Max();
            await repo.AddAsync(NewSubscription(transaction, plan, SubscriptionStatus.Scheduled, chainEnd), cancellationToken);
            return;
        }

        if (current != null)
        {
            // Nâng Gói Đọc lên Gói Sáng tạo: hiệu lực ngay, ngày Gói Đọc còn lại không quy đổi.
            current.Status = SubscriptionStatus.Superseded;
            current.UpdatedAt = now;
            repo.Update(current);
        }

        var started = NewSubscription(transaction, plan, SubscriptionStatus.Active, paidAt);
        await repo.AddAsync(started, cancellationToken);
        await _tokenQuotaService.GrantAsync(
            userId, CreditSource.Cycle, CreditsFor(transaction, plan), started.EndAt, transaction.Id,
            cancellationToken: cancellationToken);

        if (!isUpgrade)
        {
            return;
        }

        // Lượt Add-on còn dư chuyển sang chu kỳ mới.
        var lotRepo = _unitOfWork.Repository<CreditLot>();
        var addOnLots = await lotRepo.FindAsync(
            lot => lot.UserId == userId
                   && lot.Source == CreditSource.AddOn
                   && lot.Remaining > 0
                   && lot.ExpiresAt != null && lot.ExpiresAt > now,
            cancellationToken: cancellationToken);
        foreach (var lot in addOnLots)
        {
            lot.ExpiresAt = started.EndAt;
            lot.UpdatedAt = now;
            lotRepo.Update(lot);
        }

        // Các chu kỳ đã gia hạn sớm không bị mất: dời nối tiếp sau chu kỳ mới.
        var cursor = started.EndAt;
        foreach (var item in scheduled)
        {
            item.StartAt = cursor;
            item.EndAt = cursor.AddMonths(1);
            item.UpdatedAt = now;
            repo.Update(item);
            cursor = item.EndAt;
        }
    }

    /// <summary>Sửa gói chỉ áp dụng cho giao dịch tạo sau đó: ưu tiên số lượt đã chốt trên giao dịch.</summary>
    private static int CreditsFor(PaymentTransaction transaction, SubscriptionPlan plan) =>
        transaction.AiCredits ?? plan.AiCredits;

    private static UserSubscription NewSubscription(
        PaymentTransaction transaction, SubscriptionPlan plan, SubscriptionStatus status, DateTime startAt) => new()
    {
        UserId = transaction.PayerUserId,
        PlanId = plan.Id,
        Status = status,
        StartAt = startAt,
        EndAt = startAt.AddMonths(1),
        PaymentId = transaction.Id
    };

    private async Task SendRenewalRemindersAsync(DateTime now, CancellationToken cancellationToken)
    {
        var repo = _unitOfWork.Repository<UserSubscription>();
        var remindBefore = now.Add(RenewalReminderWindow);
        var ending = await repo.FindAsync(
            item => item.Status == SubscriptionStatus.Active
                    && item.EndAt > now && item.EndAt <= remindBefore
                    && item.RenewalReminderSentAt == null,
            cancellationToken: cancellationToken);

        foreach (var subscription in ending)
        {
            var alreadyRenewed = await repo.ExistsAsync(
                item => item.UserId == subscription.UserId && item.Status == SubscriptionStatus.Scheduled,
                cancellationToken);
            if (alreadyRenewed)
            {
                continue;
            }

            var plan = await _unitOfWork.Repository<SubscriptionPlan>().GetByIdAsync(subscription.PlanId, cancellationToken);
            await _notificationService.CreateAsync(
                subscription.UserId, NotificationType.SubscriptionExpiring,
                JsonSerializer.Serialize(new
                {
                    subscriptionId = subscription.Id,
                    planName = plan?.Name ?? string.Empty,
                    endAt = subscription.EndAt
                }),
                cancellationToken);

            subscription.RenewalReminderSentAt = now;
            subscription.UpdatedAt = now;
            repo.Update(subscription);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
