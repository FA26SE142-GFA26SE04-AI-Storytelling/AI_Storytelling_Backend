using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Time;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.ContentReports.Interfaces;
using StoryPlatform.Application.Features.CreatorStatistics.DTOs;
using StoryPlatform.Application.Features.CreatorStatistics.Interfaces;
using StoryPlatform.Application.Features.CreatorTiers.DTOs;
using StoryPlatform.Application.Features.CreatorTiers.Interfaces;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.CreatorTiers.Services;

/// <summary>
/// Cấp Creator đối tác (Luồng 5, 5g). Tiêu chí tính bằng <see cref="ICreatorStatisticsService.GetPartnerProgressAsync"/>;
/// mỗi lần xét ghi một dòng creator_tier_evaluations (unique theo Creator và tháng) nên xét lại trong tháng không đổi gì.
/// Cũng là <see cref="IStoryArchiveHandler"/>: truyện của đối tác bị gỡ vì vi phạm thì Creator mất cấp ngay.
/// </summary>
/// <remarks>
/// <see cref="ICreatorStatisticsService"/> được lấy từ <see cref="IServiceProvider"/> lúc dùng, không qua constructor:
/// TokenQuotaService cần dịch vụ này để xét cấp trước khi cấp hạn mức, còn thống kê Creator lại dùng TokenQuotaService.
/// </remarks>
public class CreatorTierService : ICreatorTierService, IStoryArchiveHandler
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly INotificationService _notificationService;
    private readonly IServiceProvider _services;

    public CreatorTierService(
        IUnitOfWork unitOfWork, IAuditLogWriter auditLogWriter, INotificationService notificationService,
        IServiceProvider services)
    {
        _unitOfWork = unitOfWork;
        _auditLogWriter = auditLogWriter;
        _notificationService = notificationService;
        _services = services;
    }

    private ICreatorStatisticsService Statistics => _services.GetRequiredService<ICreatorStatisticsService>();

    public async Task<CreatorTier> EnsureEvaluatedAsync(
        int creatorUserId, DateTime now, CancellationToken cancellationToken = default)
    {
        var month = VietnamTime.StartOfMonthUtc(now);
        var evaluations = _unitOfWork.Repository<CreatorTierEvaluation>();
        if (!await evaluations.ExistsAsync(
                item => item.CreatorUserId == creatorUserId && item.PeriodMonth == month, cancellationToken))
        {
            // Chạy trong ngữ cảnh (và transaction, nếu có) của nơi gọi, không tự mở transaction: lúc cấp hoặc trừ lượt,
            // nơi gọi đã giữ khoá theo tài khoản — cùng khoá worker dùng — nên không thể xét trùng.
            try
            {
                await EvaluateCreatorsAsync([creatorUserId], now, ownTransaction: false, cancellationToken);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Chỉ xảy ra ở luồng chỉ đọc lượt (không khoá, không transaction) khi worker vừa xét cùng lúc:
                // index unique (Creator, tháng) chặn dòng thứ hai; bỏ bản nháp lỗi và dùng kết quả đã có.
                _unitOfWork.DiscardTrackedChanges();
                if (!await evaluations.ExistsAsync(
                        item => item.CreatorUserId == creatorUserId && item.PeriodMonth == month, cancellationToken))
                {
                    throw;
                }
            }
        }

        return (await _unitOfWork.Repository<UserAccount>().GetByIdAsync(creatorUserId, cancellationToken))?.CreatorTier
               ?? CreatorTier.Standard;
    }

    public async Task<int> EvaluateDueAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        var month = VietnamTime.StartOfMonthUtc(now);
        var creators = await _unitOfWork.Repository<UserAccount>().FindAsync(
            user => user.Role == UserRole.ContentCreator && user.Status == AccountStatus.Active && !user.IsDeleted,
            cancellationToken: cancellationToken);
        if (creators.Count == 0)
        {
            return 0;
        }

        var ids = creators.Select(user => user.Id).ToList();
        var done = (await _unitOfWork.Repository<CreatorTierEvaluation>().FindAsync(
                item => ids.Contains(item.CreatorUserId) && item.PeriodMonth == month, cancellationToken: cancellationToken))
            .Select(item => item.CreatorUserId)
            .ToHashSet();
        var due = ids.Where(id => !done.Contains(id)).ToList();
        return due.Count == 0 ? 0 : await EvaluateCreatorsAsync(due, now, ownTransaction: true, cancellationToken);
    }

    public async Task<CreatorTierOverviewDto> GetOverviewAsync(int creatorUserId, CancellationToken cancellationToken = default)
    {
        var creator = await RequireCreatorAsync(creatorUserId, cancellationToken);
        var progress = await Statistics.GetPartnerProgressAsync([creator.Id], DateTime.UtcNow, cancellationToken);
        var evaluations = (await _unitOfWork.Repository<CreatorTierEvaluation>().FindAsync(
                item => item.CreatorUserId == creator.Id, cancellationToken: cancellationToken))
            .OrderByDescending(item => item.PeriodMonth)
            .Take(12)
            .Select(item => new CreatorTierEvaluationDto
            {
                PeriodMonth = item.PeriodMonth,
                PublishedStories = item.PublishedStories,
                CompletionRate = item.CompletionRate,
                ValidReads = item.ValidReads,
                ViolationRemovals = item.ViolationRemovals,
                Eligible = item.Eligible,
                TierBefore = item.TierBefore.ToString(),
                TierAfter = item.TierAfter.ToString(),
                EvaluatedAt = item.CreatedAt
            })
            .ToList();

        return new CreatorTierOverviewDto
        {
            CreatorUserId = creator.Id,
            Tier = creator.CreatorTier.ToString(),
            PartnerSince = creator.PartnerSince,
            Progress = progress[creator.Id],
            Evaluations = evaluations
        };
    }

    public async Task<CreatorTierOverviewDto> RevokeAsync(
        int adminUserId, int creatorUserId, RevokePartnerTierRequestDto request, CancellationToken cancellationToken = default)
    {
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length == 0)
        {
            throw new BadRequestException("Cần ghi lý do thu hồi.");
        }

        if (reason.Length > 500)
        {
            throw new BadRequestException("Lý do thu hồi tối đa 500 ký tự.");
        }

        var admin = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(adminUserId, cancellationToken);
        if (admin is not { Role: UserRole.Administrator, Status: AccountStatus.Active } || admin.IsDeleted)
        {
            throw new ForbiddenException("Chỉ Administrator đang hoạt động mới thu hồi được cấp Creator đối tác.");
        }

        var creator = await RequireCreatorAsync(creatorUserId, cancellationToken);
        if (creator.CreatorTier != CreatorTier.Partner)
        {
            throw new ConflictException("Creator này không phải Creator đối tác.");
        }

        await DemoteAsync(creator, admin.Id, "CreatorPartnerRevoked", reason, cancellationToken);
        return await GetOverviewAsync(creator.Id, cancellationToken);
    }

    /// <summary>Truyện công khai của Creator đối tác bị gỡ vì vi phạm (mọi lý do trừ SupervisorPreference): mất cấp ngay.</summary>
    public async Task HandleAsync(StoryArchiveContext context, CancellationToken cancellationToken = default)
    {
        if (context.Story.Scope != StoryScope.Creator || context.Reason == ArchivedReason.SupervisorPreference)
        {
            return;
        }

        var author = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(context.Story.AuthorUserId, cancellationToken);
        if (author is not { Role: UserRole.ContentCreator, CreatorTier: CreatorTier.Partner })
        {
            return;
        }

        await DemoteAsync(author, context.ActorUserId, "CreatorPartnerLostByViolation",
            $"{context.Reason}: storyId {context.Story.Id}", cancellationToken);
    }

    /// <summary>
    /// Xét các Creator cho tháng của <paramref name="now"/>. <paramref name="ownTransaction"/>: mỗi Creator một transaction
    /// có khoá theo tài khoản (worker). Khi false (gọi từ <see cref="EnsureEvaluatedAsync"/>) chỉ lưu vào ngữ cảnh của nơi
    /// gọi — mở transaction ở đây sẽ commit sớm transaction của nơi gọi.
    /// </summary>
    private async Task<int> EvaluateCreatorsAsync(
        IReadOnlyCollection<int> creatorUserIds, DateTime now, bool ownTransaction, CancellationToken cancellationToken)
    {
        var month = VietnamTime.StartOfMonthUtc(now);
        var previousMonth = VietnamTime.StartOfMonthUtc(month.AddDays(-1));
        var progress = await Statistics.GetPartnerProgressAsync(creatorUserIds, now, cancellationToken);
        var users = _unitOfWork.Repository<UserAccount>();
        var evaluations = _unitOfWork.Repository<CreatorTierEvaluation>();
        var changes = new List<(UserAccount Creator, CreatorTier Before, CreatorTier After, PartnerProgressDto Progress)>();
        var evaluated = 0;

        foreach (var creatorUserId in creatorUserIds)
        {
            if (ownTransaction)
            {
                await _unitOfWork.BeginTransactionAsync(cancellationToken);
            }

            try
            {
                if (ownTransaction)
                {
                    await _unitOfWork.AcquireTransactionLockAsync(creatorUserId, cancellationToken);
                }

                var creator = await users.GetByIdAsync(creatorUserId, cancellationToken);
                if (creator is not { Role: UserRole.ContentCreator, Status: AccountStatus.Active } || creator.IsDeleted
                    || await evaluations.ExistsAsync(
                        item => item.CreatorUserId == creatorUserId && item.PeriodMonth == month, cancellationToken))
                {
                    if (ownTransaction)
                    {
                        await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                    }

                    continue;
                }

                var result = progress[creatorUserId];
                var previous = await evaluations.FirstOrDefaultAsync(
                    item => item.CreatorUserId == creatorUserId && item.PeriodMonth == previousMonth,
                    cancellationToken: cancellationToken);
                var before = creator.CreatorTier;
                var after = Decide(before, result.IsEligible, previous);

                await evaluations.AddAsync(new CreatorTierEvaluation
                {
                    CreatorUserId = creatorUserId,
                    PeriodMonth = month,
                    WindowStart = result.WindowStart,
                    WindowEnd = result.WindowEnd,
                    PublishedStories = (int)Criterion(result, "published_stories"),
                    CompletionRate = Criterion(result, "completion_rate"),
                    ValidReads = (int)Criterion(result, "valid_reads"),
                    ViolationRemovals = (int)Criterion(result, "no_violation_removals"),
                    Eligible = result.IsEligible,
                    TierBefore = before,
                    TierAfter = after
                }, cancellationToken);

                if (after != before)
                {
                    creator.CreatorTier = after;
                    creator.PartnerSince = after == CreatorTier.Partner ? now : null;
                    creator.UpdatedAt = now;
                    users.Update(creator);
                    changes.Add((creator, before, after, result));
                }

                if (ownTransaction)
                {
                    await _unitOfWork.CommitTransactionAsync(cancellationToken);
                }
                else
                {
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }

                evaluated++;
            }
            catch when (ownTransaction)
            {
                await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
                _unitOfWork.DiscardTrackedChanges();
                throw;
            }
        }

        foreach (var (creator, before, after, result) in changes)
        {
            await _auditLogWriter.LogAsync(
                null, after == CreatorTier.Partner ? "CreatorPartnerGranted" : "CreatorPartnerLost",
                nameof(UserAccount), creator.Id,
                beforeState: new { tier = before.ToString() },
                afterState: new { tier = after.ToString(), month, criteria = result.Criteria },
                cancellationToken: cancellationToken);
            await NotifyAsync(creator.Id, after, reason: after == CreatorTier.Partner ? "criteria_met" : "criteria_missed_twice",
                cancellationToken);
        }

        return evaluated;
    }

    /// <summary>
    /// Đạt cả 4 tiêu chí → đối tác. Đối tác không đạt: tháng trước cũng không đạt (khi đang là đối tác) → về Creator thường,
    /// còn không thì giữ cấp thêm một tháng. Creator thường không đạt → giữ nguyên.
    /// </summary>
    public static CreatorTier Decide(CreatorTier before, bool eligible, CreatorTierEvaluation? previousMonth)
    {
        if (eligible)
        {
            return CreatorTier.Partner;
        }

        if (before != CreatorTier.Partner)
        {
            return CreatorTier.Standard;
        }

        var missedLastMonth = previousMonth is { Eligible: false, TierAfter: CreatorTier.Partner };
        return missedLastMonth ? CreatorTier.Standard : CreatorTier.Partner;
    }

    private async Task DemoteAsync(
        UserAccount creator, int? actorUserId, string action, string reason, CancellationToken cancellationToken)
    {
        creator.CreatorTier = CreatorTier.Standard;
        creator.PartnerSince = null;
        creator.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.Repository<UserAccount>().Update(creator);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogWriter.LogAsync(
            actorUserId, action, nameof(UserAccount), creator.Id,
            beforeState: new { tier = CreatorTier.Partner.ToString() },
            afterState: new { tier = CreatorTier.Standard.ToString(), reason },
            cancellationToken: cancellationToken);
        await NotifyAsync(creator.Id, CreatorTier.Standard,
            action == "CreatorPartnerRevoked" ? "revoked_by_admin" : "story_removed_for_violation", cancellationToken);
    }

    private Task NotifyAsync(int creatorUserId, CreatorTier tier, string reason, CancellationToken cancellationToken) =>
        _notificationService.CreateAsync(
            creatorUserId, NotificationType.CreatorTierChanged,
            JsonSerializer.Serialize(new { tier = tier.ToString(), reason }), cancellationToken);

    private static decimal Criterion(PartnerProgressDto progress, string key) =>
        progress.Criteria.FirstOrDefault(item => item.Key == key)?.Current ?? 0m;

    private async Task<UserAccount> RequireCreatorAsync(int creatorUserId, CancellationToken cancellationToken)
    {
        var creator = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(creatorUserId, cancellationToken);
        if (creator == null || creator.IsDeleted || creator.Role != UserRole.ContentCreator)
        {
            throw new NotFoundException("Tài khoản Content Creator", creatorUserId);
        }

        return creator;
    }
}
