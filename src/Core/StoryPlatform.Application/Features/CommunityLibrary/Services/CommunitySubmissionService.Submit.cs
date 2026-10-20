using System.Text.Json;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.Moderation.DTOs;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.CommunityLibrary.Services;

// Nộp truyện lên Community (WF-L4F UC-L4F-01).
public partial class CommunitySubmissionService
{
    public async Task<CommunitySubmitResultDto> SubmitAsync(
        int actorUserId, CommunitySubmitRequestDto request, CancellationToken cancellationToken = default)
    {
        var actor = await RequireParentAsync(actorUserId, cancellationToken);
        if (!request.ConfirmNoPersonalInfo)
        {
            throw new BadRequestException(
                "Bạn cần xác nhận truyện không chứa thông tin định danh của bé hoặc gia đình.");
        }

        var story = await LoadStoryAsync(request.StoryId, cancellationToken);
        var evaluation = await EvaluateAsync(story, actor, request, cancellationToken);
        if (evaluation.Dto.Blockers.Any(blocker => blocker.Code == CommunityBlockerCodes.AlreadySubmitted))
        {
            throw new ConflictException(
                evaluation.Dto.Blockers.First(blocker => blocker.Code == CommunityBlockerCodes.AlreadySubmitted).Message);
        }

        if (!evaluation.Dto.CanSubmit)
        {
            throw new BadRequestException(
                "Chưa thể nộp: " + string.Join(" ", evaluation.Dto.Blockers.Select(blocker => blocker.Message)));
        }

        var version = evaluation.Version!;
        var storyId = story.Id;
        var rejected = (await _unitOfWork.Repository<ModerationItem>().FindAsync(
                item => item.ItemType == ModerationItemType.CommunitySubmission
                        && item.StoryId == storyId
                        && item.Status == ModerationItemStatus.Rejected,
                cancellationToken: cancellationToken))
            .OrderByDescending(item => item.Id).FirstOrDefault();

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var queued = await _queueService.EnqueueAsync(new EnqueueModerationItemCommand
            {
                ItemType = ModerationItemType.CommunitySubmission,
                StoryId = story.Id,
                StoryVersionId = version.Id,
                CreatedByUserId = actor.Id,
                SourceChildProfileId = story.ChildProfileId,
                IsResubmission = rejected != null,
                PreviousItemId = rejected?.Id
            }, cancellationToken);

            // Chỉ lưu loại và vị trí, không lưu chữ thô của người dùng.
            queued.CopyrightDeclaration = evaluation.Declaration;
            queued.PiiCheckResult = JsonSerializer.Serialize(new
            {
                checkedAt = DateTime.UtcNow,
                storyVersionId = version.Id,
                findings = evaluation.Dto.PiiFindings.Select(finding => new { finding.Kind, finding.Location })
            });
            _unitOfWork.Repository<ModerationItem>().Update(queued);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await _auditLogWriter.LogAsync(
                actor.Id, "CommunitySubmissionSubmitted", nameof(ModerationItem), queued.Id,
                beforeState: null,
                afterState: new
                {
                    storyId = story.Id,
                    storyVersionId = version.Id,
                    copyrightDeclaration = evaluation.Declaration?.ToString(),
                    isResubmission = rejected != null
                },
                cancellationToken: cancellationToken);

            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            return new CommunitySubmitResultDto
            {
                ItemId = queued.Id,
                StoryId = story.Id,
                StoryVersionId = version.Id,
                Status = "Pending"
            };
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }
    }
}
