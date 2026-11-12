using StoryPlatform.Application.Features.PlanBenefits.Interfaces;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.TokenQuota.DTOs;
using StoryPlatform.Application.Features.TokenQuota.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.TokenQuota.Services;

/// <summary>
/// Sổ lượt AI (Luồng 2, 2d): mỗi lần cấp lượt là một lô trong credit_lots, lượt tính theo tài khoản phụ huynh.
/// Lô còn hạn = Remaining > 0 và (ExpiresAt null hoặc ExpiresAt > now); lô sắp hết hạn được dùng trước.
/// </summary>
public partial class TokenQuotaService : ITokenQuotaService
{
    private const string OutOfCreditsMessage =
        "Bạn đã hết lượt tạo truyện AI. Hãy nâng lên Gói Sáng tạo hoặc mua Add-on để tạo thêm.";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogWriter? _auditLogWriter;
    private readonly IPlanSettingsProvider? _planSettings;
    private readonly IPlanBenefitService? _planBenefits;
    private readonly IPlanUsageService? _planUsage;

    /// <param name="auditLogWriter">Chỉ cần cho thao tác của Admin (<see cref="AdjustAsync"/>).</param>
    /// <param name="planSettings">Lượt dùng thử và hạn mức tháng Creator do Admin cấu hình; null thì dùng mặc định.</param>
    /// <param name="planBenefits">Quyền lợi hiển thị kèm lượt; null thì không trả quyền lợi.</param>
    public TokenQuotaService(
        IUnitOfWork unitOfWork, IAuditLogWriter? auditLogWriter = null, IPlanSettingsProvider? planSettings = null,
        IPlanBenefitService? planBenefits = null, IPlanUsageService? planUsage = null)
    {
        _planBenefits = planBenefits;
        _planUsage = planUsage;
        _unitOfWork = unitOfWork;
        _auditLogWriter = auditLogWriter;
        _planSettings = planSettings;
    }

    private async Task<PlanSettings> SettingsAsync(CancellationToken cancellationToken) =>
        _planSettings == null ? new PlanSettings() : await _planSettings.GetAsync(cancellationToken);

    public async Task EnsureWithinQuotaAsync(int childProfileId, CancellationToken cancellationToken = default)
    {
        // Ngoài lô dùng thử (cấp một lần), phương thức này không ghi gì: việc trừ lượt chỉ diễn ra ở
        // IncrementUsageAsync, trong transaction của nơi gọi.
        var child = await GetChildAsync(childProfileId, cancellationToken);
        await EnsureTrialGrantedAsync(child.OwnerUserId, cancellationToken);

        var lots = await GetUsableLotsAsync(child.OwnerUserId, DateTime.UtcNow, cancellationToken);
        if (lots.Sum(lot => lot.Remaining) <= 0)
        {
            throw new ConflictException(OutOfCreditsMessage);
        }
    }

    public async Task IncrementUsageAsync(int childProfileId, CancellationToken cancellationToken = default)
    {
        var child = await GetChildAsync(childProfileId, cancellationToken);

        // Khoá theo tài khoản trước khi đọc lô để hai yêu cầu đồng thời không cùng tiêu một lượt.
        // Dùng chung không gian khoá với khoá truyện; trùng số chỉ gây chờ thừa.
        await _unitOfWork.AcquireTransactionLockAsync(child.OwnerUserId, cancellationToken);

        var lots = await GetUsableLotsAsync(child.OwnerUserId, DateTime.UtcNow, cancellationToken);
        var lot = OrderForUse(lots)
            .FirstOrDefault()
            ?? throw new ConflictException(OutOfCreditsMessage);

        lot.Remaining -= 1;
        lot.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.Repository<CreditLot>().Update(lot);
        await RecordUsageAsync(lot, 1, CreditUsageReason.StoryRequest, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<TokenQuotaStatusDto> GetStatusForChildAsync(
        int requestingUserId, int childProfileId, CancellationToken cancellationToken = default)
    {
        var child = await GetChildAsync(childProfileId, cancellationToken);

        var requester = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(requestingUserId, cancellationToken);
        var isAdministrator = requester?.Role == UserRole.Administrator;
        if (!isAdministrator && (child.IsDeleted || child.Status == ChildProfileStatus.Deleted || child.OwnerUserId != requestingUserId))
        {
            throw new ForbiddenException("Bạn không có quyền xem quota của hồ sơ trẻ này.");
        }

        // Lượt tính theo tài khoản chủ hồ sơ; giữ dạng DTO cũ cho màn Trang chủ và Studio.
        var quota = await GetMyQuotaAsync(child.OwnerUserId, cancellationToken);
        return new TokenQuotaStatusDto
        {
            IsUnlimited = false,
            Scope = "Personal",
            QuotaLimit = quota.TotalRemaining,
            QuotaUsed = 0,
            Remaining = quota.TotalRemaining,
            PeriodStart = quota.Plan == null ? null : DateOnly.FromDateTime(quota.Plan.StartAt),
            PeriodEnd = quota.Plan == null ? null : DateOnly.FromDateTime(quota.Plan.EndAt)
        };
    }

    public async Task<MyQuotaDto> GetMyQuotaAsync(int userId, CancellationToken cancellationToken = default)
    {
        await EnsureTrialGrantedAsync(userId, cancellationToken);
        return await BuildQuotaAsync(userId, cancellationToken);
    }

    public async Task<MyQuotaDto> GetQuotaForUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        // Admin chỉ xem: không cấp lượt dùng thử thay cho phụ huynh.
        _ = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException("Tài khoản", userId);
        return await BuildQuotaAsync(userId, cancellationToken);
    }

    public async Task<CreditAdjustmentDto> AdjustAsync(
        int adminUserId, AdjustCreditsRequestDto request, CancellationToken cancellationToken = default)
    {
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length == 0)
        {
            throw new BadRequestException("Cần ghi lý do điều chỉnh lượt.");
        }

        if (reason.Length > 500)
        {
            throw new BadRequestException("Lý do điều chỉnh không được quá 500 ký tự.");
        }

        if (request.Amount == 0)
        {
            throw new BadRequestException("Số lượt điều chỉnh phải khác 0.");
        }

        var users = _unitOfWork.Repository<UserAccount>();
        var user = await users.GetByIdAsync(request.UserId, cancellationToken)
                   ?? throw new NotFoundException("Tài khoản", request.UserId);
        if (user.Role != UserRole.User)
        {
            throw new BadRequestException("Chỉ điều chỉnh được lượt của tài khoản phụ huynh.");
        }

        var now = DateTime.UtcNow;
        var lotRepo = _unitOfWork.Repository<CreditLot>();
        CreditAdjustment adjustment;
        int before, after;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            // Cùng khoá với IncrementUsageAsync: trừ lượt và yêu cầu tạo truyện không cùng tiêu một lượt.
            await _unitOfWork.AcquireTransactionLockAsync(user.Id, cancellationToken);

            var lots = await GetUsableLotsAsync(user.Id, now, cancellationToken);
            before = lots.Sum(lot => lot.Remaining);

            if (request.Amount > 0)
            {
                await lotRepo.AddAsync(
                    new CreditLot
                    {
                        UserId = user.Id,
                        CreditType = CreditType.AI,
                        Source = CreditSource.AdminAdjust,
                        Total = request.Amount,
                        Remaining = request.Amount
                    },
                    cancellationToken);
            }
            else
            {
                var toDeduct = -request.Amount;
                if (toDeduct > before)
                {
                    throw new BadRequestException(
                        $"Không thể trừ {toDeduct} lượt: tài khoản chỉ còn {before} lượt.");
                }

                foreach (var lot in OrderForUse(lots))
                {
                    if (toDeduct == 0)
                    {
                        break;
                    }

                    var taken = Math.Min(lot.Remaining, toDeduct);
                    lot.Remaining -= taken;
                    lot.UpdatedAt = now;
                    lotRepo.Update(lot);
                    await RecordUsageAsync(lot, taken, CreditUsageReason.AdminDeduction, cancellationToken);
                    toDeduct -= taken;
                }
            }

            after = before + request.Amount;
            adjustment = new CreditAdjustment
            {
                UserId = user.Id,
                Amount = request.Amount,
                Reason = reason,
                AdminId = adminUserId
            };
            await _unitOfWork.Repository<CreditAdjustment>().AddAsync(adjustment, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _unitOfWork.DiscardTrackedChanges();
            throw;
        }

        if (_auditLogWriter != null)
        {
            await _auditLogWriter.LogAsync(
                adminUserId, "CreditAdjusted", nameof(CreditAdjustment), adjustment.Id,
                new { totalRemaining = before },
                new { totalRemaining = after, amount = request.Amount, reason, userId = user.Id },
                cancellationToken);
        }

        var admin = await users.GetByIdAsync(adminUserId, cancellationToken);
        var dto = MapToDto(adjustment, admin);
        dto.TotalRemainingAfter = after;
        return dto;
    }

    public async Task<PagedResult<CreditAdjustmentDto>> ListAdjustmentsAsync(
        int? userId, int pageIndex, int pageSize, CancellationToken cancellationToken = default)
    {
        pageIndex = Math.Max(pageIndex, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var (items, totalCount) = await _unitOfWork.Repository<CreditAdjustment>().GetPagedAsync(
            pageIndex, pageSize,
            filter: item => userId == null || item.UserId == userId,
            orderBy: source => source.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Id),
            includeProperties: "Admin",
            cancellationToken: cancellationToken);

        var users = _unitOfWork.Repository<UserAccount>();
        var result = new List<CreditAdjustmentDto>(items.Count);
        foreach (var item in items)
        {
            result.Add(MapToDto(item, item.Admin ?? await users.GetByIdAsync(item.AdminId, cancellationToken)));
        }

        return new PagedResult<CreditAdjustmentDto>(result, totalCount, pageIndex, pageSize);
    }

    private async Task<MyQuotaDto> BuildQuotaAsync(int userId, CancellationToken cancellationToken)
    {
        var quota = await BuildCreditsAsync(userId, cancellationToken);
        if (_planBenefits != null)
        {
            quota.Benefits = await _planBenefits.GetForUserAsync(userId, cancellationToken);
        }

        if (_planUsage != null)
        {
            var now = DateTime.UtcNow;
            quota.NewLibraryStoriesThisWeek = await _planUsage.CountNewLibraryStoriesThisWeekAsync(userId, now, cancellationToken);
            quota.WritingAssistsToday = await _planUsage.CountWritingAssistsTodayAsync(userId, now, cancellationToken);
        }

        return quota;
    }

    private async Task<MyQuotaDto> BuildCreditsAsync(int userId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var lots = await GetUsableLotsAsync(userId, now, cancellationToken);
        int Remaining(CreditSource source) => lots.Where(lot => lot.Source == source).Sum(lot => lot.Remaining);

        var subscriptions = await _unitOfWork.Repository<UserSubscription>().FindAsync(
            item => item.UserId == userId
                    && (item.Status == SubscriptionStatus.Active || item.Status == SubscriptionStatus.Scheduled),
            includeProperties: "Plan", cancellationToken: cancellationToken);
        var current = subscriptions.FirstOrDefault(
            item => item.Status == SubscriptionStatus.Active && item.EndAt > now);
        var next = subscriptions
            .Where(item => item.Status == SubscriptionStatus.Scheduled && item.EndAt > now)
            .OrderBy(item => item.StartAt)
            .FirstOrDefault();

        return new MyQuotaDto
        {
            Plan = MapPlan(current),
            NextPlan = MapPlan(next),
            TrialCredits = Remaining(CreditSource.Trial),
            CycleCredits = Remaining(CreditSource.Cycle),
            AddOnCredits = Remaining(CreditSource.AddOn),
            OtherCredits = Remaining(CreditSource.Legacy) + Remaining(CreditSource.AdminAdjust),
            TotalRemaining = lots.Sum(lot => lot.Remaining)
        };
    }

    public async Task<bool> GrantAsync(
        int userId, CreditSource source, int amount, DateTime? expiresAt, int? paymentId,
        CreditType creditType = CreditType.AI, CancellationToken cancellationToken = default)
    {
        if (amount <= 0)
        {
            return false;
        }

        // Một giao dịch cấp tối đa một lô cho mỗi loại lượt (Gói Creator cấp cả lượt AI lẫn lượt gửi duyệt).
        var repo = _unitOfWork.Repository<CreditLot>();
        if (paymentId.HasValue
            && await repo.ExistsAsync(lot => lot.PaymentId == paymentId && lot.CreditType == creditType, cancellationToken))
        {
            return false;
        }

        await repo.AddAsync(
            new CreditLot
            {
                UserId = userId,
                CreditType = creditType,
                Source = source,
                Total = amount,
                Remaining = amount,
                ExpiresAt = expiresAt,
                PaymentId = paymentId
            },
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<ChildProfile> GetChildAsync(int childProfileId, CancellationToken cancellationToken) =>
        await _unitOfWork.Repository<ChildProfile>().GetByIdAsync(childProfileId, cancellationToken)
        ?? throw new NotFoundException("Hồ sơ trẻ", childProfileId);

    private async Task<IReadOnlyList<CreditLot>> GetUsableLotsAsync(
        int userId, DateTime now, CancellationToken cancellationToken, CreditType creditType = CreditType.AI) =>
        await _unitOfWork.Repository<CreditLot>().FindAsync(
            lot => lot.UserId == userId
                   && lot.CreditType == creditType
                   && lot.Remaining > 0
                   && (lot.ExpiresAt == null || lot.ExpiresAt > now),
            cancellationToken: cancellationToken);

    /// <summary>
    /// Lượt dùng thử (2h): cấp 1 lượt, một lần duy nhất, không hết hạn, khi tài khoản phụ huynh có hồ sơ trẻ
    /// Active đầu tiên. Cấp kiểu lười ở lần đầu hệ thống tính lượt cho tài khoản.
    /// </summary>
    private async Task EnsureTrialGrantedAsync(int userId, CancellationToken cancellationToken)
    {
        var user = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(userId, cancellationToken);
        if (user?.Role != UserRole.User)
        {
            return;
        }

        var alreadyGranted = await _unitOfWork.Repository<CreditLot>().ExistsAsync(
            lot => lot.UserId == userId && lot.Source == CreditSource.Trial, cancellationToken);
        if (alreadyGranted)
        {
            return;
        }

        var hasActiveChild = await _unitOfWork.Repository<ChildProfile>().ExistsAsync(
            child => child.OwnerUserId == userId && child.Status == ChildProfileStatus.Active && !child.IsDeleted,
            cancellationToken);
        if (!hasActiveChild)
        {
            return;
        }

        await GrantAsync(userId, CreditSource.Trial, (await SettingsAsync(cancellationToken)).TrialAiCredits, expiresAt: null, paymentId: null, cancellationToken: cancellationToken);
    }

    /// <summary>Ghi một dòng lịch sử trừ lượt; nơi gọi lưu cùng lúc với thay đổi của lô.</summary>
    private async Task RecordUsageAsync(
        CreditLot lot, int amount, CreditUsageReason reason, CancellationToken cancellationToken) =>
        await _unitOfWork.Repository<CreditUsage>().AddAsync(
            new CreditUsage { UserId = lot.UserId, LotId = lot.Id, Amount = amount, Reason = reason },
            cancellationToken);

    /// <summary>Lô sắp hết hạn dùng trước; lô không hết hạn xếp cuối.</summary>
    private static IEnumerable<CreditLot> OrderForUse(IEnumerable<CreditLot> lots) =>
        lots.OrderBy(item => item.ExpiresAt ?? DateTime.MaxValue).ThenBy(item => item.Id);

    private static CreditAdjustmentDto MapToDto(CreditAdjustment adjustment, UserAccount? admin) => new()
    {
        Id = adjustment.Id,
        UserId = adjustment.UserId,
        Amount = adjustment.Amount,
        Reason = adjustment.Reason,
        AdminId = adjustment.AdminId,
        AdminName = string.IsNullOrWhiteSpace(admin?.FullName) ? admin?.Username ?? string.Empty : admin.FullName,
        CreatedAt = adjustment.CreatedAt
    };

    private static QuotaPlanDto? MapPlan(UserSubscription? subscription) =>
        subscription == null
            ? null
            : new QuotaPlanDto
            {
                PlanType = subscription.Plan?.PlanType.ToString() ?? string.Empty,
                PlanName = subscription.Plan?.Name ?? string.Empty,
                StartAt = subscription.StartAt,
                EndAt = subscription.EndAt
            };
}
