using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ContentReports;
using StoryPlatform.Application.Features.StoryRatings.DTOs;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.StoryRatings.Services;

public partial class StoryRatingService
{
    /// <summary>Creator xem đánh giá đang hiện của truyện mình (kể cả khi truyện đã thôi công khai).</summary>
    public async Task<StoryRatingListDto> GetForCreatorAsync(
        int creatorUserId, int storyId, StoryRatingQueryDto query, CancellationToken cancellationToken = default)
    {
        var creator = await RequireActorAsync(creatorUserId, UserRole.ContentCreator, cancellationToken);
        var story = await _unitOfWork.Repository<Story>().GetByIdAsync(storyId, cancellationToken);
        if (story == null || story.IsDeleted || story.Scope != StoryScope.Creator || story.AuthorUserId != creator.Id)
        {
            throw new NotFoundException("Truyện", storyId);
        }

        return await BuildListAsync(story.Id, query, cancellationToken);
    }

    /// <summary>Báo cáo một đánh giá đang hiện; gộp vào mục hàng đợi đang mở của đánh giá nếu có.</summary>
    public async Task<StoryRatingReportResultDto> ReportAsync(
        int userId, int ratingId, ReportStoryRatingRequestDto request, CancellationToken cancellationToken = default)
    {
        var actor = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(userId, cancellationToken);
        if (actor == null || actor.IsDeleted || actor.Status != AccountStatus.Active
            || actor.Role is not (UserRole.User or UserRole.ContentCreator))
        {
            throw new ForbiddenException("Chỉ phụ huynh hoặc Creator đang hoạt động mới báo cáo được đánh giá.");
        }

        if (!Enum.IsDefined(request.Reason))
        {
            throw new BadRequestException("Lý do báo cáo không hợp lệ.");
        }

        var trimmed = request.Note?.Trim();
        var note = string.IsNullOrEmpty(trimmed) ? null : ModeratorTextMasker.Mask(trimmed, null);
        if (trimmed?.Length > StoryRatingRules.MaxReportNoteLength || note?.Length > StoryRatingRules.MaxReportNoteLength)
        {
            throw new BadRequestException("Ghi chú báo cáo tối đa 500 ký tự.");
        }

        var rating = await _unitOfWork.Repository<StoryRating>().GetByIdAsync(ratingId, cancellationToken);
        var story = rating == null || rating.IsDeleted
            ? null
            : await _unitOfWork.Repository<Story>().GetByIdAsync(rating.StoryId, cancellationToken);
        if (rating == null || rating.IsDeleted || story == null || story.IsDeleted)
        {
            throw new NotFoundException("Đánh giá", ratingId);
        }

        if (actor.Role == UserRole.ContentCreator && story.AuthorUserId != actor.Id)
        {
            throw new ForbiddenException("Creator chỉ báo cáo được đánh giá của truyện mình.");
        }

        if (actor.Role == UserRole.User && !StoryRatingRules.IsPublicCreatorStory(story))
        {
            throw new NotFoundException("Đánh giá", ratingId);
        }

        if (rating.ReviewerUserId == actor.Id)
        {
            throw new BadRequestException("Không thể báo cáo đánh giá của chính mình.");
        }

        if (rating.Status != StoryRatingStatus.Published)
        {
            throw new BadRequestException("Chỉ báo cáo được đánh giá đang hiển thị.");
        }

        var reports = _unitOfWork.Repository<StoryRatingReport>();
        if (await reports.ExistsAsync(
                report => report.StoryRatingId == rating.Id && report.ReporterUserId == actor.Id, cancellationToken))
        {
            throw new ConflictException("Bạn đã báo cáo đánh giá này.");
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var item = await EnsureOpenItemAsync(rating, SourceReport, null, cancellationToken);
            var report = new StoryRatingReport
            {
                StoryRatingId = rating.Id,
                ReporterUserId = actor.Id,
                Reason = request.Reason,
                Note = note,
                ModerationItemId = item.Id,
                Status = StoryRatingReportStatus.Open
            };
            await reports.AddAsync(report, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _audit.LogAsync(
                actor.Id, "StoryRatingReported", nameof(StoryRatingReport), report.Id,
                beforeState: null,
                afterState: new { ratingId = rating.Id, itemId = item.Id, reason = request.Reason.ToString() },
                cancellationToken: cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }

        return new StoryRatingReportResultDto { RatingId = rating.Id };
    }
}
