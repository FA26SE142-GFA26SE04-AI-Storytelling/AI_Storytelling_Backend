using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Application.Features.StoryRatings.DTOs;
using StoryPlatform.Application.Features.StoryRatings.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.StoryRatings.Services;

/// <summary>Moderator xử lý mục StoryRating đang giữ: duyệt (hiện / giữ đánh giá) hoặc ẩn đánh giá.</summary>
public class StoryRatingModerationService : IStoryRatingModerationService
{
    public const string DecisionApproved = "Approved";
    public const string DecisionHidden = "Hidden";

    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationService _notifications;
    private readonly IAuditLogWriter _audit;

    public StoryRatingModerationService(
        IUnitOfWork unitOfWork, INotificationService notifications, IAuditLogWriter audit)
    {
        _unitOfWork = unitOfWork;
        _notifications = notifications;
        _audit = audit;
    }

    private sealed record Context(UserAccount Actor, ModerationItem Item, StoryRating Rating, Story Story);

    public async Task<StoryRatingModerationDetailDto> GetDetailAsync(
        int actorUserId, int itemId, CancellationToken cancellationToken = default)
    {
        var context = await LoadClaimedAsync(actorUserId, itemId, cancellationToken);
        var reviewer = await _unitOfWork.Repository<UserAccount>()
            .GetByIdAsync(context.Rating.ReviewerUserId, cancellationToken);
        var reports = await _unitOfWork.Repository<StoryRatingReport>().FindAsync(
            report => report.ModerationItemId == context.Item.Id && !report.IsDeleted,
            cancellationToken: cancellationToken);
        var (source, reasonCode) = ReadFlags(context.Item.Flags);

        await _audit.LogAsync(
            context.Actor.Id, "StoryRatingOpened", nameof(ModerationItem), context.Item.Id,
            beforeState: null, afterState: new { ratingId = context.Rating.Id },
            cancellationToken: cancellationToken);

        return new StoryRatingModerationDetailDto
        {
            ItemId = context.Item.Id,
            RatingId = context.Rating.Id,
            StoryId = context.Story.Id,
            StoryTitle = context.Story.Title,
            ReviewerName = StoryRatingRules.FormatReviewerName(reviewer),
            Rating = context.Rating.Rating,
            Comment = context.Rating.Comment,
            RatingStatus = context.Rating.Status.ToString(),
            HoldSource = source,
            ScreeningReasonCode = reasonCode,
            Reports = reports
                .OrderBy(report => report.CreatedAt)
                .Select(report => new StoryRatingReportItemDto
                {
                    Reason = report.Reason.ToString(), Note = report.Note, CreatedAt = report.CreatedAt
                })
                .ToList(),
            CreatedAt = context.Rating.CreatedAt,
            UpdatedAt = context.Rating.UpdatedAt
        };
    }

    public Task<StoryRatingDecisionResultDto> ApproveAsync(
        int actorUserId, int itemId, CancellationToken cancellationToken = default) =>
        DecideAsync(actorUserId, itemId, hide: false, reason: null, cancellationToken);

    public Task<StoryRatingDecisionResultDto> HideAsync(
        int actorUserId, int itemId, HideStoryRatingRequestDto request, CancellationToken cancellationToken = default)
    {
        var reason = request?.Reason?.Trim();
        if (string.IsNullOrEmpty(reason))
        {
            throw new BadRequestException("Lý do ẩn đánh giá là bắt buộc.");
        }

        if (reason.Length > StoryRatingRules.MaxHiddenReasonLength)
        {
            throw new BadRequestException("Lý do ẩn đánh giá tối đa 1000 ký tự.");
        }

        return DecideAsync(actorUserId, itemId, hide: true, reason, cancellationToken);
    }

    private async Task<StoryRatingDecisionResultDto> DecideAsync(
        int actorUserId, int itemId, bool hide, string? reason, CancellationToken cancellationToken)
    {
        var (actor, item, rating, story) = await LoadClaimedAsync(actorUserId, itemId, cancellationToken);
        var now = DateTime.UtcNow;
        var before = rating.Status;
        var wasPending = before == StoryRatingStatus.PendingModeration;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            if (hide)
            {
                rating.Status = StoryRatingStatus.Hidden;
                rating.HiddenReason = reason;
            }
            else if (wasPending)
            {
                rating.Status = StoryRatingStatus.Published;
                rating.HiddenReason = null;
            }

            rating.ModeratedByUserId = actor.Id;
            rating.ModeratedAt = now;
            _unitOfWork.Repository<StoryRating>().Update(rating);

            var reports = _unitOfWork.Repository<StoryRatingReport>();
            var openReports = await reports.FindAsync(
                report => report.ModerationItemId == item.Id && report.Status == StoryRatingReportStatus.Open && !report.IsDeleted,
                cancellationToken: cancellationToken);
            foreach (var report in openReports)
            {
                report.Status = hide ? StoryRatingReportStatus.Hidden : StoryRatingReportStatus.Approved;
                report.ResolvedAt = now;
                reports.Update(report);
            }

            item.Status = hide ? ModerationItemStatus.Rejected : ModerationItemStatus.Approved;
            item.DecidedByUserId = actor.Id;
            item.DecidedAt = now;
            item.DecisionReason = reason;
            item.RowVersion += 1;
            _unitOfWork.Repository<ModerationItem>().Update(item);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (hide || wasPending)
            {
                await _notifications.CreateAsync(
                    rating.ReviewerUserId,
                    NotificationType.StoryRatingModerated,
                    JsonSerializer.Serialize(new
                    {
                        storyId = story.Id,
                        ratingId = rating.Id,
                        decision = hide ? DecisionHidden : DecisionApproved,
                        reason
                    }),
                    cancellationToken);
            }

            if (!hide && wasPending)
            {
                await _notifications.CreateAsync(
                    story.AuthorUserId,
                    NotificationType.StoryRatingPublished,
                    JsonSerializer.Serialize(new { storyId = story.Id, ratingId = rating.Id, rating = rating.Rating }),
                    cancellationToken);
            }

            await _audit.LogAsync(
                actor.Id, hide ? "StoryRatingHidden" : "StoryRatingApproved", nameof(ModerationItem), item.Id,
                beforeState: new { ratingStatus = before.ToString() },
                afterState: new { ratingStatus = rating.Status.ToString(), ratingId = rating.Id, reason, reports = openReports.Count },
                cancellationToken: cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }

        return new StoryRatingDecisionResultDto
        {
            ItemId = item.Id,
            RatingId = rating.Id,
            Decision = hide ? DecisionHidden : DecisionApproved,
            RatingStatus = rating.Status.ToString(),
            DecidedAt = now
        };
    }

    /// <summary>Cổng chung: Moderator đang hoạt động, mục StoryRating đang do chính mình giữ và còn hạn.</summary>
    private async Task<Context> LoadClaimedAsync(int actorUserId, int itemId, CancellationToken cancellationToken)
    {
        var actor = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(actorUserId, cancellationToken);
        if (actor == null || actor.IsDeleted || actor.Status != AccountStatus.Active || actor.Role != UserRole.Moderator)
        {
            throw new ForbiddenException("Chỉ Moderator đang hoạt động mới kiểm duyệt được đánh giá.");
        }

        var item = await _unitOfWork.Repository<ModerationItem>().GetByIdAsync(itemId, cancellationToken);
        if (item == null || item.IsDeleted)
        {
            throw new NotFoundException("Mục kiểm duyệt", itemId);
        }

        if (item.ItemType != ModerationItemType.StoryRating || item.StoryRatingId == null)
        {
            throw new BadRequestException("Mục này không phải mục kiểm duyệt đánh giá.");
        }

        if (item.Status != ModerationItemStatus.Claimed || item.ClaimedByUserId != actor.Id)
        {
            throw new ForbiddenException("Bạn chỉ được xử lý mục đang do chính mình nhận.");
        }

        if (item.ClaimedAt == null || item.ClaimedAt.Value.Add(ModerationQueuePolicy.ClaimTimeout) <= DateTime.UtcNow)
        {
            throw new ConflictException(
                "Mục đã quá 2 giờ kể từ lúc nhận và sẽ được trả về hàng đợi. Vui lòng nhận lại.");
        }

        var rating = await _unitOfWork.Repository<StoryRating>().GetByIdAsync(item.StoryRatingId.Value, cancellationToken);
        if (rating == null || rating.IsDeleted)
        {
            throw new ConflictException("Đánh giá đã bị người viết xóa.");
        }

        var story = await _unitOfWork.Repository<Story>().GetByIdAsync(rating.StoryId, cancellationToken)
                    ?? throw new NotFoundException("Truyện", rating.StoryId);
        return new Context(actor, item, rating, story);
    }

    private static (string Source, string? ReasonCode) ReadFlags(string? flags)
    {
        if (string.IsNullOrWhiteSpace(flags))
        {
            return (StoryRatingService.SourceReport, null);
        }

        using var json = JsonDocument.Parse(flags);
        var root = json.RootElement;
        var source = root.TryGetProperty("source", out var sourceValue) && sourceValue.ValueKind == JsonValueKind.String
            ? sourceValue.GetString()!
            : StoryRatingService.SourceReport;
        var reasonCode = root.TryGetProperty("reasonCode", out var codeValue) && codeValue.ValueKind == JsonValueKind.String
            ? codeValue.GetString()
            : null;
        return (source, reasonCode);
    }
}
