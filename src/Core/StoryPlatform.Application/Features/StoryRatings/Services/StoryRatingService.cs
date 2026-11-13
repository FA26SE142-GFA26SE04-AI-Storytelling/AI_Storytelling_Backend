using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Moderation.DTOs;
using StoryPlatform.Application.Features.Moderation.Interfaces;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Application.Features.StoryRatings.DTOs;
using StoryPlatform.Application.Features.StoryRatings.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.StoryRatings.Services;

/// <summary>Phụ huynh đánh giá truyện Creator công khai (spec 2026-11-12-danh-gia-truyen-creator).</summary>
public partial class StoryRatingService : IStoryRatingService
{
    public const string ReadingRequired = "RATING_REQUIRES_READING";
    public const string SourceScreening = "Screening";
    public const string SourceReport = "Report";
    public const string DeletedClosedReason = "RatingDeleted";

    private const string CommentTooLong = "Nhận xét tối đa 1000 ký tự.";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IModerationQueueService _queue;
    private readonly INotificationService _notifications;
    private readonly IAuditLogWriter _audit;

    public StoryRatingService(
        IUnitOfWork unitOfWork,
        IModerationQueueService queue,
        INotificationService notifications,
        IAuditLogWriter audit)
    {
        _unitOfWork = unitOfWork;
        _queue = queue;
        _notifications = notifications;
        _audit = audit;
    }

    public async Task<StoryRatingListDto> GetForStoryAsync(
        int parentUserId, int storyId, StoryRatingQueryDto query, CancellationToken cancellationToken = default)
    {
        var parent = await RequireActorAsync(parentUserId, UserRole.User, cancellationToken);
        var story = await RequirePublicStoryAsync(storyId, cancellationToken);

        var list = await BuildListAsync(story.Id, query, cancellationToken);
        var mine = await FindMineAsync(parent.Id, story.Id, cancellationToken);
        list.MyRating = mine == null ? null : MapMine(mine, parent);
        list.CanRate = await HasChildReadAsync(parent.Id, story.Id, cancellationToken);
        return list;
    }

    public async Task<MyStoryRatingDto> UpsertMineAsync(
        int parentUserId, int storyId, UpsertStoryRatingRequestDto request, CancellationToken cancellationToken = default)
    {
        var parent = await RequireActorAsync(parentUserId, UserRole.User, cancellationToken);
        var story = await RequirePublicStoryAsync(storyId, cancellationToken);
        if (request.Rating is < StoryRatingRules.MinRating or > StoryRatingRules.MaxRating)
        {
            throw new BadRequestException("Số sao phải từ 1 đến 5.");
        }

        if (request.Comment?.Trim().Length > StoryRatingRules.MaxCommentLength)
        {
            throw new BadRequestException(CommentTooLong);
        }

        if (!await HasChildReadAsync(parent.Id, story.Id, cancellationToken))
        {
            throw new ForbiddenException(ReadingRequired);
        }

        var screening = StoryRatingScreening.Screen(request.Rating, request.Comment);
        if (screening.Comment?.Length > StoryRatingRules.MaxCommentLength)
        {
            // Nhãn che số điện thoại dài hơn số gốc.
            throw new BadRequestException(CommentTooLong);
        }

        var repository = _unitOfWork.Repository<StoryRating>();
        StoryRating rating;
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var existing = await FindMineAsync(parent.Id, story.Id, cancellationToken);
            var before = existing?.Status;
            rating = existing ?? new StoryRating { StoryId = story.Id, ReviewerUserId = parent.Id };
            rating.Rating = request.Rating;
            rating.Comment = screening.Comment;
            rating.HiddenReason = null;

            // Đang chờ hoặc bị ẩn thì sửa xong vẫn phải chờ Moderator, kể cả khi lọc thấy sạch.
            var mustWait = screening.NeedsModeration
                           || before is StoryRatingStatus.PendingModeration or StoryRatingStatus.Hidden;
            rating.Status = mustWait ? StoryRatingStatus.PendingModeration : StoryRatingStatus.Published;

            if (existing == null)
            {
                await repository.AddAsync(rating, cancellationToken);
            }
            else
            {
                repository.Update(rating);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (mustWait)
            {
                await EnsureOpenItemAsync(rating, SourceScreening, screening.ReasonCode, cancellationToken);
            }
            else if (before != StoryRatingStatus.Published)
            {
                await NotifyCreatorAsync(story.AuthorUserId, rating, cancellationToken);
            }

            await _audit.LogAsync(
                parent.Id,
                existing == null ? "StoryRatingCreated" : "StoryRatingUpdated",
                nameof(StoryRating),
                rating.Id,
                beforeState: before == null ? null : new { status = before.ToString() },
                afterState: new
                {
                    status = rating.Status.ToString(),
                    rating = rating.Rating,
                    screening = screening.ReasonCode,
                    screeningVersion = StoryRatingScreening.Version
                },
                cancellationToken: cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }

        return MapMine(rating, parent);
    }

    public async Task<bool> DeleteMineAsync(int parentUserId, int storyId, CancellationToken cancellationToken = default)
    {
        var parent = await RequireActorAsync(parentUserId, UserRole.User, cancellationToken);
        var rating = await FindMineAsync(parent.Id, storyId, cancellationToken)
                     ?? throw new NotFoundException("Đánh giá của bạn cho truyện", storyId);
        var before = rating.Status;
        rating.IsDeleted = true;
        _unitOfWork.Repository<StoryRating>().Update(rating);

        // FirstOrDefaultAsync đọc không theo dõi; lấy lại bằng GetByIdAsync để RowVersion so đúng giá trị gốc.
        var open = await FindOpenItemAsync(rating.Id, cancellationToken);
        var item = open == null
            ? null
            : await _unitOfWork.Repository<ModerationItem>().GetByIdAsync(open.Id, cancellationToken);
        if (item != null)
        {
            item.Status = ModerationItemStatus.Closed;
            item.ClosedReason = DeletedClosedReason;
            item.RowVersion += 1;
            _unitOfWork.Repository<ModerationItem>().Update(item);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync(
            parent.Id, "StoryRatingDeleted", nameof(StoryRating), rating.Id,
            beforeState: new { status = before.ToString() },
            afterState: new { deleted = true, closedItemId = item?.Id },
            cancellationToken: cancellationToken);
        return true;
    }

    private async Task<UserAccount> RequireActorAsync(int userId, UserRole role, CancellationToken cancellationToken)
    {
        var user = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(userId, cancellationToken);
        if (user == null || user.IsDeleted || user.Status != AccountStatus.Active || user.Role != role)
        {
            throw new ForbiddenException("Tài khoản không có quyền dùng chức năng đánh giá truyện.");
        }

        return user;
    }

    private async Task<Story> RequirePublicStoryAsync(int storyId, CancellationToken cancellationToken)
    {
        var story = await _unitOfWork.Repository<Story>().GetByIdAsync(storyId, cancellationToken);
        if (story == null || !StoryRatingRules.IsPublicCreatorStory(story))
        {
            throw new NotFoundException("Truyện", storyId);
        }

        return story;
    }

    private Task<StoryRating?> FindMineAsync(int parentId, int storyId, CancellationToken cancellationToken) =>
        _unitOfWork.Repository<StoryRating>().FirstOrDefaultAsync(
            rating => rating.StoryId == storyId && rating.ReviewerUserId == parentId && !rating.IsDeleted,
            cancellationToken: cancellationToken);

    private Task<ModerationItem?> FindOpenItemAsync(int ratingId, CancellationToken cancellationToken) =>
        _unitOfWork.Repository<ModerationItem>().FirstOrDefaultAsync(
            item => item.ItemType == ModerationItemType.StoryRating
                    && item.StoryRatingId == ratingId
                    && !item.IsDeleted
                    && (item.Status == ModerationItemStatus.Pending || item.Status == ModerationItemStatus.Claimed),
            cancellationToken: cancellationToken);

    private async Task<bool> HasChildReadAsync(int parentId, int storyId, CancellationToken cancellationToken)
    {
        var childIds = (await _unitOfWork.Repository<ChildProfile>().FindAsync(
                child => child.OwnerUserId == parentId && child.Status == ChildProfileStatus.Active && !child.IsDeleted,
                cancellationToken: cancellationToken))
            .Select(child => child.Id)
            .ToList();

        return childIds.Count > 0 && await _unitOfWork.Repository<ReadingSession>().ExistsAsync(
            session => session.StoryId == storyId && childIds.Contains(session.ChildProfileId) && !session.IsDeleted,
            cancellationToken);
    }

    /// <summary>Mỗi đánh giá tối đa một mục mở; báo cáo / lần lọc sau gộp vào mục đó.</summary>
    private async Task<ModerationItem> EnsureOpenItemAsync(
        StoryRating rating, string source, string? reasonCode, CancellationToken cancellationToken)
    {
        var open = await FindOpenItemAsync(rating.Id, cancellationToken);
        if (open != null)
        {
            return open;
        }

        var item = await _queue.EnqueueAsync(new EnqueueModerationItemCommand
        {
            ItemType = ModerationItemType.StoryRating,
            StoryId = rating.StoryId,
            StoryRatingId = rating.Id,
            CreatedByUserId = rating.ReviewerUserId
        }, cancellationToken);
        item.Flags = JsonSerializer.Serialize(new { source, reasonCode });
        _unitOfWork.Repository<ModerationItem>().Update(item);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return item;
    }

    private Task NotifyCreatorAsync(int creatorUserId, StoryRating rating, CancellationToken cancellationToken) =>
        _notifications.CreateAsync(
            creatorUserId,
            NotificationType.StoryRatingPublished,
            JsonSerializer.Serialize(new { storyId = rating.StoryId, ratingId = rating.Id, rating = rating.Rating }),
            cancellationToken);

    private async Task<StoryRatingListDto> BuildListAsync(
        int storyId, StoryRatingQueryDto query, CancellationToken cancellationToken)
    {
        var repository = _unitOfWork.Repository<StoryRating>();
        var published = await repository.FindAsync(
            rating => rating.StoryId == storyId && rating.Status == StoryRatingStatus.Published && !rating.IsDeleted,
            cancellationToken: cancellationToken);

        var pageIndex = Math.Max(query.PageIndex, 1);
        var (items, totalCount) = await repository.GetPagedAsync(
            pageIndex: pageIndex,
            pageSize: query.PageSize,
            filter: rating => rating.StoryId == storyId && rating.Status == StoryRatingStatus.Published && !rating.IsDeleted,
            orderBy: Order(query.OrderBy),
            cancellationToken: cancellationToken);

        var reviewerIds = items.Select(rating => rating.ReviewerUserId).Distinct().ToList();
        var reviewers = (await _unitOfWork.Repository<UserAccount>().FindAsync(
                user => reviewerIds.Contains(user.Id), cancellationToken: cancellationToken))
            .ToDictionary(user => user.Id);

        var dtos = items
            .Select(rating => Fill(new StoryRatingDto(), rating, reviewers.GetValueOrDefault(rating.ReviewerUserId)))
            .ToList();
        return new StoryRatingListDto
        {
            StoryId = storyId,
            Summary = Summarize(published),
            Ratings = new PagedResult<StoryRatingDto>(dtos, totalCount, pageIndex, query.PageSize)
        };
    }

    private static StoryRatingSummaryDto Summarize(IReadOnlyCollection<StoryRating> published) => new()
    {
        AverageRating = published.Count == 0
            ? null
            : Math.Round(published.Average(rating => rating.Rating), 1, MidpointRounding.AwayFromZero),
        RatingCount = published.Count,
        Distribution = Enumerable.Range(StoryRatingRules.MinRating, StoryRatingRules.MaxRating)
            .ToDictionary(star => star, star => published.Count(rating => rating.Rating == star))
    };

    private static Func<IQueryable<StoryRating>, IOrderedQueryable<StoryRating>> Order(string? orderBy) =>
        (orderBy ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            StoryRatingQueryDto.Highest => query => query
                .OrderByDescending(rating => rating.Rating).ThenByDescending(rating => rating.CreatedAt).ThenByDescending(rating => rating.Id),
            StoryRatingQueryDto.Lowest => query => query
                .OrderBy(rating => rating.Rating).ThenByDescending(rating => rating.CreatedAt).ThenByDescending(rating => rating.Id),
            _ => query => query.OrderByDescending(rating => rating.CreatedAt).ThenByDescending(rating => rating.Id)
        };

    private static T Fill<T>(T dto, StoryRating rating, UserAccount? reviewer) where T : StoryRatingDto
    {
        dto.RatingId = rating.Id;
        dto.ReviewerName = StoryRatingRules.FormatReviewerName(reviewer);
        dto.Rating = rating.Rating;
        dto.Comment = rating.Comment;
        dto.CreatedAt = rating.CreatedAt;
        dto.UpdatedAt = rating.UpdatedAt;
        return dto;
    }

    private static MyStoryRatingDto MapMine(StoryRating rating, UserAccount reviewer)
    {
        var dto = Fill(new MyStoryRatingDto(), rating, reviewer);
        dto.Status = rating.Status.ToString();
        dto.HiddenReason = rating.Status == StoryRatingStatus.Hidden ? rating.HiddenReason : null;
        return dto;
    }
}
