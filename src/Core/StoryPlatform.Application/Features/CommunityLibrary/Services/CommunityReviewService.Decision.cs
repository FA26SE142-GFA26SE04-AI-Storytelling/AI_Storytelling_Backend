using System.Text.Json;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.CommunityLibrary.Services;

// Quyết định duyệt Community: Approve và Reject (WF-L4F UC-L4F-02).
public partial class CommunityReviewService
{
    public async Task<CommunityReviewDecisionResultDto> ApproveAsync(
        int actorUserId, int itemId, CancellationToken cancellationToken = default)
    {
        var context = await LoadClaimedContextAsync(actorUserId, itemId, cancellationToken);

        // Kiểm lại mọi điều kiện tại thời điểm Approve (BR-L4F-02: truyện chứa thông tin định danh phải bị Reject).
        var nickname = context.SourceChild?.Nickname;
        var findings = await CommunityPiiScanner.ScanAsync(
            _unitOfWork, context.Story, context.Version, nickname, cancellationToken);
        var labels = await LoadLabelsAsync(context.Story.Id, cancellationToken);
        var checklist = CommunityReviewState.ReadChecklist(context.Item.ReviewProgress);
        var blockers = EvaluateBlockers(context, checklist, findings, labels.Count);
        if (blockers.Count > 0)
        {
            throw new BadRequestException(
                "Chưa thể Approve: " + string.Join(" ", blockers.Select(blocker => blocker.Message)));
        }

        return await DecideAsync(
            context, ModerationItemStatus.Approved, "approved", null,
            CommunityReviewState.WriteFeedback("approved", null, checklist),
            "CommunitySubmissionApproved", cancellationToken);
    }

    public async Task<CommunityReviewDecisionResultDto> RejectAsync(
        int actorUserId, int itemId, RejectCommunityRequestDto request, CancellationToken cancellationToken = default)
    {
        var context = await LoadClaimedContextAsync(actorUserId, itemId, cancellationToken);

        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length == 0 || reason.Length > CommunityReviewPolicy.MaxReasonLength)
        {
            throw new BadRequestException(
                $"Lý do từ chối bắt buộc và tối đa {CommunityReviewPolicy.MaxReasonLength} ký tự.");
        }

        var checklist = CommunityReviewState.ReadChecklist(context.Item.ReviewProgress);
        return await DecideAsync(
            context, ModerationItemStatus.Rejected, "rejected", reason,
            CommunityReviewState.WriteFeedback("rejected", reason, checklist),
            "CommunitySubmissionRejected", cancellationToken);
    }

    /// <summary>Đóng mục, ghi audit và báo người nộp trong một transaction.</summary>
    private async Task<CommunityReviewDecisionResultDto> DecideAsync(
        ReviewContext context,
        ModerationItemStatus itemStatus,
        string decisionKey,
        string? reason,
        string feedbackJson,
        string auditAction,
        CancellationToken cancellationToken)
    {
        var item = context.Item;
        var now = DateTime.UtcNow;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            item.Status = itemStatus;
            item.DecidedByUserId = context.Actor.Id;
            item.DecidedAt = now;
            item.DecisionReason = reason;
            item.Feedback = feedbackJson;
            item.RowVersion += 1;
            _unitOfWork.Repository<ModerationItem>().Update(item);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await _auditLogWriter.LogAsync(
                context.Actor.Id, auditAction, nameof(ModerationItem), item.Id,
                beforeState: new { status = ModerationItemStatus.Claimed.ToString() },
                afterState: new
                {
                    status = itemStatus.ToString(),
                    storyId = context.Story.Id,
                    storyVersionId = context.Version.Id,
                    reason
                },
                cancellationToken: cancellationToken);

            await NotifySubmitterAsync(context, decisionKey, reason, cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }

        return new CommunityReviewDecisionResultDto
        {
            ItemId = item.Id,
            Decision = itemStatus.ToString(),
            StoryId = context.Story.Id,
            DecidedAt = now
        };
    }

    /// <summary>community_library_reviewed cho người nộp; không nêu Moderator.</summary>
    private async Task NotifySubmitterAsync(
        ReviewContext context, string decisionKey, string? reason, CancellationToken cancellationToken)
    {
        var submitterId = context.Item.CreatedByUserId;
        if (!submitterId.HasValue)
        {
            return;
        }

        var submitter = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(submitterId.Value, cancellationToken);
        if (submitter == null || submitter.IsDeleted || submitter.Status != AccountStatus.Active)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            itemId = context.Item.Id,
            storyId = context.Story.Id,
            decision = decisionKey,
            reason
        });
        await _notificationService.CreateAsync(
            submitterId.Value, NotificationType.CommunityLibraryReviewed, payload, cancellationToken);
    }
}
