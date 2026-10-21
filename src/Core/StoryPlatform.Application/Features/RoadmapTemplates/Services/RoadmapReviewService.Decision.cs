using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.RoadmapTemplates.DTOs;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.RoadmapTemplates.Services;

public partial class RoadmapReviewService
{
    public async Task<RoadmapReviewDecisionResultDto> ApproveAsync(
        int actorUserId, int itemId, CancellationToken cancellationToken = default)
    {
        var context = await LoadClaimedContextAsync(actorUserId, itemId, cancellationToken);
        var checks = await _checker.EvaluateAsync(context.Roadmap, context.Items, cancellationToken);
        var checklist = RoadmapReviewState.ReadChecklist(context.Item.ReviewProgress);
        var blockers = EvaluateApproveBlockers(context, checks, checklist);
        if (blockers.Count > 0)
        {
            throw new BadRequestException(
                "Chưa thể Approve: " + string.Join(" ", blockers.Select(blocker => blocker.Message)));
        }

        var roadmap = context.Roadmap;
        var item = context.Item;
        var actor = context.Actor;
        var now = DateTime.UtcNow;
        int? replacedRoadmapId = null;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            if (roadmap.ClonedFromId.HasValue)
            {
                var source = await _unitOfWork.Repository<ReadingRoadmap>()
                    .GetByIdAsync(roadmap.ClonedFromId.Value, cancellationToken);
                if (source != null && !source.IsDeleted
                    && source.Status == RoadmapTemplateStatus.Published
                    && source.ArchivedAt == null)
                {
                    source.Status = RoadmapTemplateStatus.Archived;
                    source.ArchivedReason = RoadmapArchiveReason.ReplacedByClone;
                    source.ArchivedAt = now;
                    source.ArchivedByUserId = actor.Id;
                    _unitOfWork.Repository<ReadingRoadmap>().Update(source);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    await _auditLogWriter.LogAsync(
                        actor.Id, "RoadmapTemplateArchived", nameof(ReadingRoadmap), source.Id,
                        beforeState: new { status = RoadmapTemplateStatus.Published.ToString() },
                        afterState: new
                        {
                            status = RoadmapTemplateStatus.Archived.ToString(),
                            reason = RoadmapArchiveReason.ReplacedByClone.ToString(),
                            replacedByRoadmapId = roadmap.Id
                        },
                        cancellationToken: cancellationToken);
                    replacedRoadmapId = source.Id;
                }
            }

            roadmap.Status = RoadmapTemplateStatus.Published;
            roadmap.PublishedAt = now;
            roadmap.PublishedByUserId = actor.Id;
            _unitOfWork.Repository<ReadingRoadmap>().Update(roadmap);

            item.Status = ModerationItemStatus.Approved;
            item.DecidedByUserId = actor.Id;
            item.DecidedAt = now;
            item.DecisionReason = null;
            item.Feedback = RoadmapReviewState.WriteFeedback(new RoadmapReviewFeedback(
                RoadmapReviewPolicy.DecisionApproved, null, null, [], checklist));
            item.RowVersion += 1;
            _unitOfWork.Repository<ModerationItem>().Update(item);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await _auditLogWriter.LogAsync(
                actor.Id, "RoadmapTemplateApproved", nameof(ReadingRoadmap), roadmap.Id,
                beforeState: new { status = RoadmapTemplateStatus.PendingReview.ToString() },
                afterState: new
                {
                    status = RoadmapTemplateStatus.Published.ToString(),
                    moderationItemId = item.Id,
                    replacedRoadmapId
                },
                cancellationToken: cancellationToken);
            await NotifySubmitterAsync(context, RoadmapReviewPolicy.DecisionApproved, null, cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }

        return new RoadmapReviewDecisionResultDto
        {
            ItemId = item.Id,
            Decision = ModerationItemStatus.Approved.ToString(),
            RoadmapId = roadmap.Id,
            RoadmapStatus = roadmap.Status.ToString(),
            ReplacedRoadmapId = replacedRoadmapId,
            DecidedAt = now
        };
    }

    public async Task<RoadmapReviewDecisionResultDto> RequestChangesAsync(
        int actorUserId,
        int itemId,
        RequestRoadmapChangesDto request,
        CancellationToken cancellationToken = default)
    {
        var context = await LoadClaimedContextAsync(actorUserId, itemId, cancellationToken);
        var generalNote = request.GeneralNote?.Trim() ?? string.Empty;
        if (generalNote.Length == 0 || generalNote.Length > RoadmapReviewPolicy.MaxTextLength)
        {
            throw new BadRequestException(
                $"Nhận xét chung bắt buộc và tối đa {RoadmapReviewPolicy.MaxTextLength} ký tự.");
        }

        var parts = ValidateParts(request.Parts, context);
        var checklist = RoadmapReviewState.ReadChecklist(context.Item.ReviewProgress);
        var feedback = new RoadmapReviewFeedback(
            RoadmapReviewPolicy.DecisionChangesRequested, generalNote, null, parts, checklist);
        return await DecideAsync(
            context, ModerationItemStatus.ChangesRequested,
            RoadmapReviewPolicy.DecisionChangesRequested,
            generalNote, feedback, "RoadmapTemplateChangesRequested", cancellationToken);
    }

    public async Task<RoadmapReviewDecisionResultDto> RejectAsync(
        int actorUserId,
        int itemId,
        RejectRoadmapRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var context = await LoadClaimedContextAsync(actorUserId, itemId, cancellationToken);
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length == 0 || reason.Length > RoadmapReviewPolicy.MaxTextLength)
        {
            throw new BadRequestException(
                $"Lý do từ chối bắt buộc và tối đa {RoadmapReviewPolicy.MaxTextLength} ký tự.");
        }

        var checklist = RoadmapReviewState.ReadChecklist(context.Item.ReviewProgress);
        var feedback = new RoadmapReviewFeedback(
            RoadmapReviewPolicy.DecisionRejected, null, reason, [], checklist);
        return await DecideAsync(
            context, ModerationItemStatus.Rejected, RoadmapReviewPolicy.DecisionRejected,
            reason, feedback, "RoadmapTemplateRejected", cancellationToken);
    }

    private static List<RoadmapChangePartDto> ValidateParts(
        List<RoadmapChangePartDto>? parts, ReviewContext context)
    {
        var input = parts ?? [];
        if (input.Count > RoadmapReviewPolicy.MaxParts)
        {
            throw new BadRequestException($"Tối đa {RoadmapReviewPolicy.MaxParts} phần cần sửa.");
        }

        var roadmapStoryIds = context.Items.Select(item => item.StoryId).ToList();
        var result = new List<RoadmapChangePartDto>();
        var seen = new HashSet<(string Type, int? StoryId)>();
        foreach (var part in input)
        {
            var type = part.PartType?.Trim() ?? string.Empty;
            var note = part.Note?.Trim() ?? string.Empty;
            if (note.Length == 0 || note.Length > RoadmapReviewPolicy.MaxNoteLength)
            {
                throw new BadRequestException(
                    $"Ghi chú của mỗi phần cần sửa bắt buộc và tối đa "
                    + $"{RoadmapReviewPolicy.MaxNoteLength} ký tự.");
            }

            string? action = null;
            switch (type)
            {
                case RoadmapReviewPolicy.PartStory:
                    if (!part.StoryId.HasValue || !roadmapStoryIds.Contains(part.StoryId.Value))
                    {
                        throw new BadRequestException("Phần \"story\" phải chỉ một truyện thuộc bộ.");
                    }

                    action = part.Action?.Trim();
                    if (action != RoadmapReviewPolicy.ActionRemove
                        && action != RoadmapReviewPolicy.ActionReplace)
                    {
                        throw new BadRequestException("Phần \"story\" phải có Action là remove hoặc replace.");
                    }

                    break;
                case RoadmapReviewPolicy.PartOrder:
                case RoadmapReviewPolicy.PartNameDescription:
                case RoadmapReviewPolicy.PartAgeLevel:
                    if (part.StoryId.HasValue || !string.IsNullOrWhiteSpace(part.Action))
                    {
                        throw new BadRequestException($"Phần \"{type}\" không có StoryId hoặc Action.");
                    }

                    break;
                default:
                    throw new BadRequestException(
                        $"PartType \"{type}\" không hợp lệ (order, story, name_description, age_level)."
                    );
            }

            if (!seen.Add((type, part.StoryId)))
            {
                throw new BadRequestException("Có phần cần sửa bị trùng.");
            }

            result.Add(new RoadmapChangePartDto
            {
                PartType = type,
                StoryId = part.StoryId,
                Action = action,
                Note = note
            });
        }

        return result;
    }

    private async Task<RoadmapReviewDecisionResultDto> DecideAsync(
        ReviewContext context,
        ModerationItemStatus itemStatus,
        string decisionKey,
        string reason,
        RoadmapReviewFeedback feedback,
        string auditAction,
        CancellationToken cancellationToken)
    {
        var roadmap = context.Roadmap;
        var item = context.Item;
        var now = DateTime.UtcNow;
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            roadmap.Status = RoadmapTemplateStatus.Draft;
            roadmap.SubmittedAt = null;
            _unitOfWork.Repository<ReadingRoadmap>().Update(roadmap);
            item.Status = itemStatus;
            item.DecidedByUserId = context.Actor.Id;
            item.DecidedAt = now;
            item.DecisionReason = reason;
            item.Feedback = RoadmapReviewState.WriteFeedback(feedback);
            item.RowVersion += 1;
            _unitOfWork.Repository<ModerationItem>().Update(item);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await _auditLogWriter.LogAsync(
                context.Actor.Id, auditAction, nameof(ReadingRoadmap), roadmap.Id,
                beforeState: new { status = RoadmapTemplateStatus.PendingReview.ToString() },
                afterState: new
                {
                    status = RoadmapTemplateStatus.Draft.ToString(),
                    moderationItemId = item.Id,
                    decision = decisionKey,
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

        return new RoadmapReviewDecisionResultDto
        {
            ItemId = item.Id,
            Decision = itemStatus.ToString(),
            RoadmapId = roadmap.Id,
            RoadmapStatus = roadmap.Status.ToString(),
            DecidedAt = now
        };
    }
}
