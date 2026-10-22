using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.Moderation.DTOs;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Moderation.Services;

public partial class PublicStoryReviewService
{
    private const int MaxHistoryEntries = 50;

    private async Task<List<PublicReviewHistoryEntryDto>> LoadReviewHistoryAsync(
        ModerationItem item, CancellationToken cancellationToken)
    {
        var history = new List<PublicReviewHistoryEntryDto>();
        var visited = new HashSet<int> { item.Id };
        var previousId = item.PreviousItemId;

        while (previousId.HasValue
               && visited.Add(previousId.Value)
               && history.Count < MaxHistoryEntries)
        {
            var previous = await _unitOfWork.Repository<ModerationItem>()
                .GetByIdAsync(previousId.Value, cancellationToken);
            if (previous == null)
            {
                break;
            }

            var decider = previous.DecidedByUserId.HasValue
                ? await _unitOfWork.Repository<UserAccount>()
                    .GetByIdAsync(previous.DecidedByUserId.Value, cancellationToken)
                : null;
            var version = previous.StoryVersionId.HasValue
                ? await _unitOfWork.Repository<StoryVersion>()
                    .GetByIdAsync(previous.StoryVersionId.Value, cancellationToken)
                : null;

            history.Add(new PublicReviewHistoryEntryDto
            {
                ItemId = previous.Id,
                StoryVersionId = previous.StoryVersionId,
                VersionNo = version?.VersionNo,
                Decision = previous.Status.ToString(),
                DecidedByUserId = previous.DecidedByUserId,
                DecidedByName = decider == null
                    ? null
                    : string.IsNullOrWhiteSpace(decider.FullName)
                        ? decider.Username
                        : decider.FullName,
                DecidedAt = previous.DecidedAt,
                Reason = previous.DecisionReason,
                Feedback = previous.Feedback
            });

            previousId = previous.PreviousItemId;
        }

        return history;
    }

    public async Task<PublicVersionComparisonDto> CompareAsync(
        int actorUserId, int itemId, string? against, CancellationToken cancellationToken = default)
    {
        var context = await LoadClaimedContextAsync(actorUserId, itemId, cancellationToken);
        var baseVersion = await ResolveComparisonBaseAsync(context, against, cancellationToken);
        var current = await PublicVersionSnapshot.LoadAsync(_unitOfWork, context.Version, cancellationToken);
        var before = await PublicVersionSnapshot.LoadAsync(_unitOfWork, baseVersion, cancellationToken);
        var result = PublicVersionComparer.Compare(before, current);
        result.Against = against!;

        await _auditLogWriter.LogAsync(
            context.Actor.Id,
            "PublicStoryReviewCompared",
            nameof(ModerationItem),
            context.Item.Id,
            beforeState: null,
            afterState: new { against, baseVersionId = baseVersion.Id, currentVersionId = context.Version.Id },
            cancellationToken: cancellationToken);
        return result;
    }

    private async Task<StoryVersion> ResolveComparisonBaseAsync(
        ReviewContext context, string? against, CancellationToken cancellationToken)
    {
        switch (against)
        {
            case PublicReviewRequirements.ComparePrevious:
                if (context.Item.PreviousItemId == null)
                {
                    throw new BadRequestException(
                        "Mục này không phải bản gửi lại nên chưa có bản đã gửi trước để so sánh.");
                }

                var previousItem = await _unitOfWork.Repository<ModerationItem>()
                                       .GetByIdAsync(context.Item.PreviousItemId.Value, cancellationToken)
                                   ?? throw new NotFoundException("Mục kiểm duyệt", context.Item.PreviousItemId.Value);
                if (previousItem.StoryId != context.Story.Id || previousItem.StoryVersionId == null)
                {
                    throw new BadRequestException("Mục trước không cùng truyện hoặc không có phiên bản để so sánh.");
                }

                return await _unitOfWork.Repository<StoryVersion>()
                           .GetByIdAsync(previousItem.StoryVersionId.Value, cancellationToken)
                       ?? throw new NotFoundException("Phiên bản truyện", previousItem.StoryVersionId.Value);

            case PublicReviewRequirements.ComparePublic:
                var approved = await _unitOfWork.Repository<StoryVersion>().FindAsync(
                    version => version.StoryId == context.Story.Id
                               && version.Moderation.ReviewStatus == VersionReviewStatus.Approved
                               && version.Id != context.Version.Id,
                    cancellationToken: cancellationToken);
                return approved.OrderByDescending(version => version.Moderation.ReviewedAt)
                           .ThenByDescending(version => version.VersionNo)
                           .FirstOrDefault()
                       ?? throw new BadRequestException("Truyện chưa có bản công khai để so sánh.");

            default:
                throw new BadRequestException("against phải là previous hoặc public.");
        }
    }
}
