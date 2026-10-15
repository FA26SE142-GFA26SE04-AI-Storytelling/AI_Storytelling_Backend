using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.Moderation.Interfaces;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Moderation.Services;

public class ModerationSweepService : IModerationSweepService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationService _notificationService;

    public ModerationSweepService(IUnitOfWork unitOfWork, INotificationService notificationService)
    {
        _unitOfWork = unitOfWork;
        _notificationService = notificationService;
    }

    public async Task<int> ReleaseExpiredClaimsAsync(CancellationToken cancellationToken = default)
    {
        var threshold = DateTime.UtcNow - ModerationQueuePolicy.ClaimTimeout;
        var expired = await _unitOfWork.Repository<ModerationItem>().FindAsync(
            item => item.Status == ModerationItemStatus.Claimed
                    && item.ClaimedAt != null && item.ClaimedAt <= threshold,
            cancellationToken: cancellationToken);
        return await ReleaseAsync(expired,
            item => item.ClaimedAt != null && item.ClaimedAt <= threshold, cancellationToken);
    }

    public async Task<int> ReleaseClaimsOfModeratorAsync(
        int moderatorUserId, CancellationToken cancellationToken = default)
    {
        var claimed = await _unitOfWork.Repository<ModerationItem>().FindAsync(
            item => item.Status == ModerationItemStatus.Claimed && item.ClaimedByUserId == moderatorUserId,
            cancellationToken: cancellationToken);
        return await ReleaseAsync(claimed,
            item => item.ClaimedByUserId == moderatorUserId, cancellationToken);
    }

    public async Task<int> WarnOverdueSlaAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var overdue = await _unitOfWork.Repository<ModerationItem>().FindAsync(
            item => (item.Status == ModerationItemStatus.Pending || item.Status == ModerationItemStatus.Claimed)
                    && item.SlaDueAt != null && item.SlaDueAt < now && item.SlaWarnedAt == null,
            cancellationToken: cancellationToken);
        if (overdue.Count == 0)
        {
            return 0;
        }

        var moderators = await _unitOfWork.Repository<UserAccount>().FindAsync(
            user => user.Role == UserRole.Moderator && user.Status == AccountStatus.Active,
            cancellationToken: cancellationToken);
        if (moderators.Count == 0)
        {
            // Chưa có ai để báo: để SlaWarnedAt trống, lần quét sau khi có Moderator sẽ gửi.
            return 0;
        }

        var repository = _unitOfWork.Repository<ModerationItem>();
        var ready = new List<ModerationItem>();
        foreach (var candidate in overdue)
        {
            // FindAsync trả bản không theo dõi. Lấy bản tracked trước khi tăng RowVersion
            // để EF giữ giá trị gốc trong điều kiện UPDATE.
            var item = await repository.GetByIdAsync(candidate.Id, cancellationToken);
            if (item == null || item.IsDeleted || item.RowVersion != candidate.RowVersion
                || item.Status is not (ModerationItemStatus.Pending or ModerationItemStatus.Claimed)
                || item.SlaDueAt == null || item.SlaDueAt >= now || item.SlaWarnedAt != null)
            {
                continue;
            }

            ready.Add(item);
        }

        if (ready.Count == 0)
        {
            return 0;
        }

        // Cùng transaction: không mất thông báo nếu một lần CreateAsync thất bại.
        // RowVersion bảo đảm hai worker không cùng đánh dấu và tạo thông báo cho một mục.
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var item in ready)
            {
                item.SlaWarnedAt = now;
                item.RowVersion += 1;
                repository.Update(item);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            foreach (var item in ready)
            {
                var payload = JsonSerializer.Serialize(new
                {
                    itemId = item.Id,
                    itemType = item.ItemType.ToString(),
                    slaDueAt = item.SlaDueAt
                });
                foreach (var moderator in moderators)
                {
                    await _notificationService.CreateAsync(
                        moderator.Id, NotificationType.ModerationSlaWarning, payload, cancellationToken);
                }
            }

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            // Scope của worker bị bỏ sau lỗi; lần quét sau tải lại trạng thái đã rollback.
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }

        return ready.Count;
    }

    private async Task<int> ReleaseAsync(
        IReadOnlyList<ModerationItem> items, Func<ModerationItem, bool> stillEligible,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return 0;
        }

        var repository = _unitOfWork.Repository<ModerationItem>();
        var count = 0;
        foreach (var candidate in items)
        {
            var item = await repository.GetByIdAsync(candidate.Id, cancellationToken);
            if (item == null || item.IsDeleted || item.RowVersion != candidate.RowVersion
                || item.Status != ModerationItemStatus.Claimed || !stillEligible(item))
            {
                continue;
            }

            item.Status = ModerationItemStatus.Pending;
            item.ClaimedByUserId = null;
            item.ClaimedAt = null;
            item.RowVersion += 1;
            repository.Update(item);
            count += 1;
        }

        if (count > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return count;
    }
}
