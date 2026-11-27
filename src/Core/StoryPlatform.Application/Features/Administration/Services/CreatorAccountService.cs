using System.Linq.Expressions;
using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.Administration.DTOs;
using StoryPlatform.Application.Features.Administration.Interfaces;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Application.Features.TokenQuota.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Application.Features.AccessControl;
using StoryPlatform.Application.Features.AccessControl.Interfaces;
using StoryPlatform.Application.Features.AccessControl.Services;

namespace StoryPlatform.Application.Features.Administration.Services;

/// <summary>
/// Admin quản lý tài khoản Content Creator (Luồng 5, M5). Vô hiệu hoá không gỡ truyện đã Published (truyện vi phạm gỡ qua
/// báo cáo nội dung, M3); yêu cầu gửi duyệt đang mở được rút như khi truyện bị lưu trữ.
/// </summary>
public class CreatorAccountService : ICreatorAccountService
{
    /// <summary>Lý do đóng mục kiểm duyệt khi Creator bị vô hiệu hoá (ClosedReason của moderation_items).</summary>
    public const string ClosedReason = "creator_deactivated";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly INotificationService _notificationService;
    private readonly ITokenQuotaService? _tokenQuotaService;

    private readonly IAccessPolicy _accessPolicy;

    public CreatorAccountService(
        IUnitOfWork unitOfWork, IAuditLogWriter auditLogWriter, INotificationService notificationService,
        ITokenQuotaService? tokenQuotaService = null,
        IAccessPolicy? accessPolicy = null)
    {
        _accessPolicy = accessPolicy ?? AccessPolicy.Default(unitOfWork);
        _unitOfWork = unitOfWork;
        _auditLogWriter = auditLogWriter;
        _notificationService = notificationService;
        _tokenQuotaService = tokenQuotaService;
    }

    public async Task<PagedResult<CreatorAccountDto>> ListAsync(
        AccountStatus? status, string? search, int pageIndex, int pageSize, CancellationToken cancellationToken = default)
    {
        pageIndex = Math.Max(pageIndex, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var term = search?.Trim().ToLower() ?? string.Empty;
        Expression<Func<UserAccount, bool>> filter = user =>
            user.Role == UserRole.ContentCreator && !user.IsDeleted
            && (status == null || user.Status == status)
            && (term == string.Empty
                || user.Username.ToLower().Contains(term)
                || user.FullName.ToLower().Contains(term)
                || user.Email.ToLower().Contains(term));

        var (items, totalCount) = await _unitOfWork.Repository<UserAccount>().GetPagedAsync(
            pageIndex, pageSize, filter,
            orderBy: source => source.OrderByDescending(user => user.CreatedAt).ThenByDescending(user => user.Id),
            cancellationToken: cancellationToken);

        var ids = items.Select(user => user.Id).ToList();
        var counts = await PublishedCountsAsync(ids, cancellationToken);
        var applications = await LatestApplicationStatusesAsync(ids, cancellationToken);
        var result = items
            .Select(user => MapToDto(user, counts.GetValueOrDefault(user.Id), applications.GetValueOrDefault(user.Id)))
            .ToList();
        return new PagedResult<CreatorAccountDto>(result, totalCount, pageIndex, pageSize);
    }

    public async Task<CreatorAccountDto> GetAsync(int creatorUserId, CancellationToken cancellationToken = default)
    {
        var creator = await RequireCreatorAsync(creatorUserId, cancellationToken);
        var counts = await PublishedCountsAsync([creator.Id], cancellationToken);
        var applications = await LatestApplicationStatusesAsync([creator.Id], cancellationToken);
        return MapToDto(creator, counts.GetValueOrDefault(creator.Id), applications.GetValueOrDefault(creator.Id));
    }

    public async Task<DeactivateCreatorResultDto> DeactivateAsync(
        int adminUserId, int creatorUserId, DeactivateCreatorRequestDto request, CancellationToken cancellationToken = default)
    {
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length == 0)
        {
            throw new BadRequestException("Cần ghi lý do vô hiệu hoá.");
        }

        if (reason.Length > 500)
        {
            throw new BadRequestException("Lý do vô hiệu hoá tối đa 500 ký tự.");
        }

        var admin = await RequireAdministratorAsync(adminUserId, cancellationToken);
        var creator = await RequireCreatorAsync(creatorUserId, cancellationToken);
        if (creator.Status == AccountStatus.Suspended)
        {
            throw new ConflictException("Tài khoản nhà sáng tạo này đã bị vô hiệu hoá.");
        }

        if (creator.Status != AccountStatus.Active)
        {
            // Creator chưa được duyệt: Admin xử lý bằng từ chối đơn đăng ký thay vì vô hiệu hoá.
            throw new ConflictException("Chỉ vô hiệu hoá được nhà sáng tạo đang hoạt động. Nhà sáng tạo chờ duyệt thì từ chối đơn đăng ký.");
        }

        var now = DateTime.UtcNow;
        List<(ModerationItem Item, ModerationItemStatus Before, int? ClaimedBy)> closed;
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            creator.Status = AccountStatus.Suspended;
            // Access token cũ hết hiệu lực ngay ở middleware; refresh token bị thu hồi bên dưới.
            creator.TokenVersion += 1;
            creator.UpdatedAt = now;
            _unitOfWork.Repository<UserAccount>().Update(creator);
            await RevokeRefreshTokensAsync(creator.Id, now, cancellationToken);
            closed = await CloseOpenSubmissionsAsync(creator.Id, now, cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            _unitOfWork.DiscardTrackedChanges();
            throw;
        }

        // Yêu cầu đang chờ bị rút: hoàn lượt gửi duyệt (Luồng 2, 2f); mục đã có người nhận bị đóng thì không hoàn.
        if (_tokenQuotaService != null)
        {
            foreach (var (item, _, _) in closed.Where(entry => entry.Item.Status == ModerationItemStatus.Withdrawn))
            {
                await _tokenQuotaService.RefundSubmissionAsync(item.Id, cancellationToken);
            }
        }

        await _auditLogWriter.LogAsync(
            admin.Id, "ContentCreatorDeactivated", nameof(UserAccount), creator.Id,
            beforeState: new { status = AccountStatus.Active.ToString() },
            afterState: new
            {
                status = AccountStatus.Suspended.ToString(),
                reason,
                withdrawnSubmissions = closed.Count(entry => entry.Item.Status == ModerationItemStatus.Withdrawn),
                closedSubmissions = closed.Count(entry => entry.Item.Status == ModerationItemStatus.Closed)
            },
            cancellationToken: cancellationToken);
        foreach (var (item, before, claimedBy) in closed)
        {
            await _auditLogWriter.LogAsync(
                admin.Id, "PublicStorySubmissionClosedByCreatorDeactivation", nameof(ModerationItem), item.Id,
                beforeState: new { status = before.ToString() },
                afterState: new { status = item.Status.ToString(), storyId = item.StoryId, creatorUserId = creator.Id },
                cancellationToken: cancellationToken);
            if (before == ModerationItemStatus.Claimed && claimedBy.HasValue)
            {
                await NotifyClaimantAsync(item, claimedBy.Value, cancellationToken);
            }
        }

        await _notificationService.CreateAsync(
            creator.Id, NotificationType.AdminRoleChanged,
            JsonSerializer.Serialize(new { action = "deactivated", reason }), cancellationToken);

        var counts = await PublishedCountsAsync([creator.Id], cancellationToken);
        return new DeactivateCreatorResultDto
        {
            Creator = MapToDto(creator, counts.GetValueOrDefault(creator.Id)),
            WithdrawnSubmissions = closed.Count(entry => entry.Item.Status == ModerationItemStatus.Withdrawn),
            ClosedSubmissions = closed.Count(entry => entry.Item.Status == ModerationItemStatus.Closed)
        };
    }

    public async Task<CreatorAccountDto> ReactivateAsync(
        int adminUserId, int creatorUserId, CancellationToken cancellationToken = default)
    {
        var admin = await RequireAdministratorAsync(adminUserId, cancellationToken);
        var creator = await RequireCreatorAsync(creatorUserId, cancellationToken);
        if (creator.Status != AccountStatus.Suspended)
        {
            throw new ConflictException("Chỉ kích hoạt lại được nhà sáng tạo đã bị vô hiệu hoá.");
        }

        creator.Status = AccountStatus.Active;
        creator.FailedLoginAttempts = 0;
        creator.LockedUntil = null;
        creator.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.Repository<UserAccount>().Update(creator);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogWriter.LogAsync(
            admin.Id, "ContentCreatorReactivated", nameof(UserAccount), creator.Id,
            beforeState: new { status = AccountStatus.Suspended.ToString() },
            afterState: new { status = AccountStatus.Active.ToString() },
            cancellationToken: cancellationToken);
        await _notificationService.CreateAsync(
            creator.Id, NotificationType.AdminRoleChanged,
            JsonSerializer.Serialize(new { action = "reactivated" }), cancellationToken);

        var counts = await PublishedCountsAsync([creator.Id], cancellationToken);
        return MapToDto(creator, counts.GetValueOrDefault(creator.Id));
    }

    /// <summary>
    /// Rút yêu cầu gửi duyệt công khai đang mở của Creator: Pending → Withdrawn, Claimed → Closed, và gỡ trạng thái chờ
    /// duyệt của phiên bản (như PendingSubmissionArchiveHandler khi truyện bị lưu trữ). Lưu cùng transaction của nơi gọi.
    /// </summary>
    private async Task<List<(ModerationItem Item, ModerationItemStatus Before, int? ClaimedBy)>> CloseOpenSubmissionsAsync(
        int creatorUserId, DateTime now, CancellationToken cancellationToken)
    {
        var items = _unitOfWork.Repository<ModerationItem>();
        var open = await items.FindAsync(
            item => item.ItemType == ModerationItemType.PublicStoryReview
                    && item.CreatedByUserId == creatorUserId
                    && (item.Status == ModerationItemStatus.Pending || item.Status == ModerationItemStatus.Claimed),
            cancellationToken: cancellationToken);

        var closed = new List<(ModerationItem Item, ModerationItemStatus Before, int? ClaimedBy)>();
        foreach (var item in open.Where(item => !item.IsDeleted))
        {
            var before = item.Status;
            var claimedBy = item.ClaimedByUserId;
            if (before == ModerationItemStatus.Pending)
            {
                item.Status = ModerationItemStatus.Withdrawn;
                item.WithdrawnAt = now;
            }
            else
            {
                item.Status = ModerationItemStatus.Closed;
                item.DecidedAt = now;
                item.ClosedReason = ClosedReason;
            }

            // RowVersion là token đồng thời: Moderator vừa quyết định cùng lúc thì SaveChanges ném 409, không ghi đè.
            item.RowVersion += 1;
            items.Update(item);

            if (item.StoryVersionId.HasValue)
            {
                var version = await _unitOfWork.Repository<StoryVersion>()
                    .GetByIdAsync(item.StoryVersionId.Value, cancellationToken);
                if (version != null && version.Moderation.ReviewStatus == VersionReviewStatus.Pending)
                {
                    version.Moderation.ReviewStatus = VersionReviewStatus.None;
                    version.Moderation.ReviewSubmittedAt = null;
                    _unitOfWork.Repository<StoryVersion>().Update(version);
                }
            }

            closed.Add((item, before, claimedBy));
        }

        return closed;
    }

    private async Task NotifyClaimantAsync(ModerationItem item, int claimantUserId, CancellationToken cancellationToken)
    {
        var claimant = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(claimantUserId, cancellationToken);
        if (claimant == null || claimant.IsDeleted || claimant.Status != AccountStatus.Active)
        {
            return;
        }

        await _notificationService.CreateAsync(
            claimant.Id, NotificationType.ContentModerationResult,
            JsonSerializer.Serialize(new
            {
                itemId = item.Id,
                itemType = ModerationItemType.PublicStoryReview.ToString(),
                storyId = item.StoryId,
                decision = "closed",
                reason = ClosedReason
            }),
            cancellationToken);
    }

    private async Task RevokeRefreshTokensAsync(int userId, DateTime now, CancellationToken cancellationToken)
    {
        var repository = _unitOfWork.Repository<RefreshToken>();
        var sessions = await repository.FindAsync(
            token => token.UserAccountId == userId && token.RevokedAt == null, cancellationToken: cancellationToken);
        foreach (var session in sessions)
        {
            session.RevokedAt = now;
            repository.Update(session);
        }
    }

    private async Task<Dictionary<int, int>> PublishedCountsAsync(IReadOnlyList<int> creatorIds, CancellationToken cancellationToken)
    {
        if (creatorIds.Count == 0)
        {
            return [];
        }

        var published = await _unitOfWork.Repository<Story>().FindAsync(
            story => creatorIds.Contains(story.AuthorUserId) && story.Scope == StoryScope.Creator
                     && story.IsPublished && !story.IsDeleted,
            cancellationToken: cancellationToken);
        return published.GroupBy(story => story.AuthorUserId).ToDictionary(group => group.Key, group => group.Count());
    }

    /// <summary>Trạng thái đơn đăng ký mới nhất của từng Creator (cùng thứ tự với CreatorApplicationService.LatestOfAsync).</summary>
    private async Task<Dictionary<int, string>> LatestApplicationStatusesAsync(
        IReadOnlyList<int> creatorIds, CancellationToken cancellationToken)
    {
        if (creatorIds.Count == 0)
        {
            return [];
        }

        var applications = await _unitOfWork.Repository<CreatorApplication>().FindAsync(
            item => creatorIds.Contains(item.ApplicantUserId), cancellationToken: cancellationToken);
        return applications
            .GroupBy(item => item.ApplicantUserId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Id).First().Status.ToString());
    }

    private Task<UserAccount> RequireAdministratorAsync(int adminUserId, CancellationToken cancellationToken) =>
        _accessPolicy.EnsureAsync(
            adminUserId, Permissions.Admin.CreatorAccountManage,
            deniedMessage: "Chỉ quản trị viên đang hoạt động mới quản lý được tài khoản nhà sáng tạo nội dung.",
            cancellationToken: cancellationToken);

    private async Task<UserAccount> RequireCreatorAsync(int creatorUserId, CancellationToken cancellationToken)
    {
        var creator = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(creatorUserId, cancellationToken);
        if (creator == null || creator.IsDeleted || creator.Role != UserRole.ContentCreator)
        {
            throw new NotFoundException("Tài khoản nhà sáng tạo nội dung", creatorUserId);
        }

        return creator;
    }

    private static CreatorAccountDto MapToDto(
        UserAccount user, int publishedStoryCount, string? latestApplicationStatus = null) => new()
    {
        Id = user.Id,
        Username = user.Username,
        FullName = user.FullName,
        Email = user.Email,
        Status = user.Status.ToString(),
        LatestApplicationStatus = latestApplicationStatus,
        PublishedStoryCount = publishedStoryCount,
        CreatedAt = user.CreatedAt,
        LastLoginAt = user.LastLoginAt
    };
}
