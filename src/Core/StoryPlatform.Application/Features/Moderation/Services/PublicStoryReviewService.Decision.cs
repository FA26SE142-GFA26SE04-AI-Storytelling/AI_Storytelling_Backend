using System.Text.Json;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.Moderation.DTOs;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Moderation.Services;

public partial class PublicStoryReviewService
{
    private static List<ApproveBlockerDto> EvaluateApproveBlockers(
        ReviewContext context, ReviewContent content)
    {
        var blockers = new List<ApproveBlockerDto>();
        var story = context.Story;
        var item = context.Item;
        var proposedIds = PublicStoryLabels.Parse(item.ProposedContentCategoryIds);
        if (proposedIds.Count == 0)
            blockers.Add(Blocker(ApproveBlockerCodes.NoLabels, "Truyện phải có ít nhất một nhãn nội dung đề xuất."));
        else if (proposedIds.Any(id => !content.ProposedCategories.Any(category => category.Id == id && category.IsActive)))
            blockers.Add(Blocker(ApproveBlockerCodes.InactiveLabels, "Nhãn đề xuất không tồn tại hoặc không còn hoạt động."));

        if (story.Status == StoryStatus.Rejected || story.ArchivedAt != null)
        {
            blockers.Add(Blocker(
                ApproveBlockerCodes.StoryArchived,
                "Truyện đã bị Archive, không thể Approve."));
        }
        else if (story.Status != StoryStatus.Ready)
        {
            blockers.Add(Blocker(
                ApproveBlockerCodes.StoryNotReady,
                "Truyện chưa sẵn sàng (media chưa hoàn tất) nên chưa thể Approve."));
        }

        if (item.CreatedByUserId == context.Actor.Id || story.AuthorUserId == context.Actor.Id)
        {
            blockers.Add(Blocker(
                ApproveBlockerCodes.OwnContent,
                "Bạn không thể duyệt nội dung do chính mình tạo hoặc nộp."));
        }

        var progress = BuildProgress(item, content);
        if (!progress.RequiredViewsComplete)
        {
            var parts = new List<string>();
            if (progress.MissingSceneIds.Count > 0)
            {
                parts.Add($"còn {progress.MissingSceneIds.Count} scene chưa xem");
            }
            if (progress.MissingWarningSegmentIds.Count > 0)
            {
                parts.Add($"còn {progress.MissingWarningSegmentIds.Count} đoạn audio có cảnh báo chưa nghe");
            }
            if (progress.NeedsAtLeastOneAudio)
            {
                parts.Add("chưa nghe thử đoạn audio nào");
            }

            blockers.Add(Blocker(
                ApproveBlockerCodes.ViewsIncomplete,
                $"Chưa xem đủ phần bắt buộc ({string.Join("; ", parts)})."));
        }

        var unresolvedFlags = BuildFlags(context.Version, item).Count(flag => !flag.Resolved);
        if (unresolvedFlags > 0)
        {
            blockers.Add(Blocker(
                ApproveBlockerCodes.FlagsUnresolved,
                $"Còn {unresolvedFlags} cờ vùng xám chưa xử lý."));
        }

        var saved = PublicReviewState.GetChecklist(item.Checklist);
        var ungraded = PublicReviewRequirements.ChecklistKeys
            .Where(key => !saved.ContainsKey(key)).ToList();
        var failed = PublicReviewRequirements.ChecklistKeys
            .Where(key => saved.TryGetValue(key, out var passed) && !passed).ToList();
        if (ungraded.Count > 0 || failed.Count > 0)
        {
            var details = new List<string>();
            if (ungraded.Count > 0)
            {
                details.Add("chưa chấm: " + string.Join(", ",
                    ungraded.Select(key => PublicReviewRequirements.ChecklistLabels[key])));
            }
            if (failed.Count > 0)
            {
                details.Add("không đạt: " + string.Join(", ",
                    failed.Select(key => PublicReviewRequirements.ChecklistLabels[key])));
            }

            blockers.Add(Blocker(
                ApproveBlockerCodes.ChecklistIncomplete,
                $"Checklist chưa đạt đủ 8 tiêu chí ({string.Join("; ", details)})."));
        }

        return blockers;
    }

    private static ApproveBlockerDto Blocker(string code, string message) =>
        new() { Code = code, Message = message };

    public async Task<PublicReviewDecisionResultDto> ApproveAsync(
        int actorUserId, int itemId, CancellationToken cancellationToken = default)
    {
        var context = await LoadClaimedContextAsync(actorUserId, itemId, cancellationToken);
        var content = await LoadContentAsync(context.Version.Id, cancellationToken);
        content = await WithProposedCategoriesAsync(content, context.Item, cancellationToken);
        var blockers = EvaluateApproveBlockers(context, content);

        var ownContent = blockers.FirstOrDefault(blocker => blocker.Code == ApproveBlockerCodes.OwnContent);
        if (ownContent != null)
        {
            throw new ForbiddenException(ownContent.Message);
        }
        if (blockers.Count > 0)
        {
            throw new BadRequestException(
                "Chưa thể Approve: " + string.Join(" ", blockers.Select(blocker => blocker.Message)));
        }

        return await DecideAsync(
            context,
            ModerationItemStatus.Approved,
            VersionReviewStatus.Approved,
            decisionKey: "approved",
            reason: null,
            feedbackJson: null,
            publish: true,
            auditAction: "PublicStoryReviewApproved",
            cancellationToken);
    }

    public async Task<PublicReviewDecisionResultDto> RequestChangesAsync(
        int actorUserId,
        int itemId,
        RequestChangesDto request,
        CancellationToken cancellationToken = default)
    {
        var context = await LoadClaimedContextAsync(actorUserId, itemId, cancellationToken);
        var generalNote = request.GeneralNote?.Trim() ?? string.Empty;
        if (generalNote.Length == 0)
        {
            throw new BadRequestException("Phải ghi chú chung khi Yêu cầu sửa.");
        }
        if (generalNote.Length > PublicReviewRequirements.MaxGeneralNoteLength)
        {
            throw new BadRequestException(
                $"Ghi chú chung tối đa {PublicReviewRequirements.MaxGeneralNoteLength} ký tự.");
        }

        var requested = request.Parts ?? new List<ChangeRequestPartDto>();
        if (requested.Count == 0)
        {
            throw new BadRequestException("Phải đánh dấu ít nhất một phần cần sửa.");
        }
        if (requested.Count > PublicReviewRequirements.MaxChangeParts)
        {
            throw new BadRequestException(
                $"Tối đa {PublicReviewRequirements.MaxChangeParts} phần cần sửa trong một lần.");
        }

        var content = await LoadContentAsync(context.Version.Id, cancellationToken);
        var parts = requested.Select(part => NormalizeChangePart(part, content)).ToList();
        var feedbackJson = JsonSerializer.Serialize(new
        {
            generalNote,
            parts = parts.Select(part => new
            {
                partType = part.PartType,
                partId = part.PartId,
                note = part.Note
            })
        });

        return await DecideAsync(
            context,
            ModerationItemStatus.ChangesRequested,
            VersionReviewStatus.ChangesRequested,
            decisionKey: "changes_requested",
            reason: generalNote,
            feedbackJson,
            publish: false,
            auditAction: "PublicStoryReviewChangesRequested",
            cancellationToken);
    }

    public async Task<PublicReviewDecisionResultDto> RejectAsync(
        int actorUserId,
        int itemId,
        RejectRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var context = await LoadClaimedContextAsync(actorUserId, itemId, cancellationToken);
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length == 0)
        {
            throw new BadRequestException("Phải ghi lý do khi Từ chối.");
        }
        if (reason.Length > PublicReviewRequirements.MaxDecisionReasonLength)
        {
            throw new BadRequestException(
                $"Lý do tối đa {PublicReviewRequirements.MaxDecisionReasonLength} ký tự.");
        }

        return await DecideAsync(
            context,
            ModerationItemStatus.Rejected,
            VersionReviewStatus.Rejected,
            decisionKey: "rejected",
            reason,
            feedbackJson: JsonSerializer.Serialize(new { reason }),
            publish: false,
            auditAction: "PublicStoryReviewRejected",
            cancellationToken);
    }

    private static ChangeRequestPartDto NormalizeChangePart(
        ChangeRequestPartDto part, ReviewContent content)
    {
        var type = part.PartType?.Trim() ?? string.Empty;
        var note = part.Note?.Trim() ?? string.Empty;

        if (PublicReviewRequirements.UnsupportedChangeParts.Contains(type))
        {
            throw new BadRequestException($"Loại phần \"{type}\" chưa được hỗ trợ.");
        }
        if (note.Length == 0 || note.Length > PublicReviewRequirements.MaxPartNoteLength)
        {
            throw new BadRequestException(
                $"Mỗi phần cần sửa phải có ghi chú (tối đa {PublicReviewRequirements.MaxPartNoteLength} ký tự).");
        }

        var id = part.PartId;
        var positionIsValid = type switch
        {
            PublicReviewRequirements.SceneTextPart
                or PublicReviewRequirements.SceneImagePart
                or PublicReviewRequirements.SceneAudioPart =>
                    id.HasValue && content.Scenes.Any(scene => scene.Id == id.Value),
            PublicReviewRequirements.QuizPart =>
                id.HasValue && content.Quizzes.Any(quiz => quiz.Id == id.Value),
            PublicReviewRequirements.VocabularyPart =>
                id.HasValue && content.Vocabulary.Any(word => word.Id == id.Value),
            PublicReviewRequirements.ListeningPart =>
                id.HasValue && content.Listening.Any(interaction => interaction.Id == id.Value),

            PublicReviewRequirements.MetadataPart => !id.HasValue,
            _ => throw new BadRequestException($"PartType \"{type}\" không hợp lệ.")
        };

        if (!positionIsValid)
        {
            throw new BadRequestException($"Vị trí cần sửa không hợp lệ cho loại phần \"{type}\".");
        }

        return new ChangeRequestPartDto { PartType = type, PartId = id, Note = note };
    }

    private async Task<PublicReviewDecisionResultDto> DecideAsync(
        ReviewContext context,
        ModerationItemStatus itemStatus,
        VersionReviewStatus versionStatus,
        string decisionKey,
        string? reason,
        string? feedbackJson,
        bool publish,
        string auditAction,
        CancellationToken cancellationToken)
    {
        var item = context.Item;
        var version = context.Version;
        var story = context.Story;
        var now = DateTime.UtcNow;
        List<int>? labelsBefore = null;
        List<int>? labelsAfter = null;

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

            version.Moderation.ReviewStatus = versionStatus;
            version.Moderation.ReviewedByUserId = context.Actor.Id;
            version.Moderation.ReviewedAt = now;
            version.Moderation.ReviewFeedback = feedbackJson;
            _unitOfWork.Repository<StoryVersion>().Update(version);

            if (publish)
            {
                var wanted = PublicStoryLabels.Parse(item.ProposedContentCategoryIds);
                var repository = _unitOfWork.Repository<StoryCategory>();
                var existing = await repository.FindAsync(x => x.StoryId == story.Id, cancellationToken: cancellationToken);
                labelsBefore = existing.Select(x => x.ContentCategoryId).OrderBy(id => id).ToList();
                foreach (var link in existing.Where(link => !wanted.Contains(link.ContentCategoryId))) repository.Delete(link);
                foreach (var id in wanted.Where(id => !existing.Any(link => link.ContentCategoryId == id)))
                    await repository.AddAsync(new StoryCategory
                    {
                        StoryId = story.Id, ContentCategoryId = id, Source = TagSource.Creator
                    }, cancellationToken);
                labelsAfter = wanted;
                story.IsPublished = true;
                story.PublishedAt ??= now;
                story.ChildVisibleAt ??= now;
                _unitOfWork.Repository<Story>().Update(story);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            // Bị từ chối thì Creator được hoàn lượt gửi duyệt; yêu cầu sửa thì không (Luồng 2, 2f).
            if (itemStatus == ModerationItemStatus.Rejected && _tokenQuotaService != null)
            {
                await _tokenQuotaService.RefundSubmissionAsync(item.Id, cancellationToken);
            }
            await _auditLogWriter.LogAsync(
                context.Actor.Id,
                auditAction,
                nameof(ModerationItem),
                item.Id,
                beforeState: new { status = ModerationItemStatus.Claimed.ToString(), labelsBefore },
                afterState: new
                {
                    status = itemStatus.ToString(),
                    storyId = story.Id,
                    storyVersionId = version.Id,
                    reason,
                    labelsAfter
                },
                cancellationToken);

            await NotifySubmitterAsync(context, decisionKey, reason, cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }

        return new PublicReviewDecisionResultDto
        {
            ItemId = item.Id,
            Decision = itemStatus.ToString(),
            StoryId = story.Id,
            StoryVersionId = version.Id,
            StoryPublished = story.IsPublished,
            DecidedAt = now
        };
    }

    private async Task NotifySubmitterAsync(
        ReviewContext context,
        string decisionKey,
        string? reason,
        CancellationToken cancellationToken)
    {
        var submitterId = context.Item.CreatedByUserId;
        if (!submitterId.HasValue)
        {
            return;
        }

        var submitter = await _unitOfWork.Repository<UserAccount>()
            .GetByIdAsync(submitterId.Value, cancellationToken);
        if (submitter == null || submitter.IsDeleted || submitter.Status != AccountStatus.Active)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            itemId = context.Item.Id,
            itemType = ModerationItemType.PublicStoryReview.ToString(),
            storyId = context.Story.Id,
            storyVersionId = context.Version.Id,
            decision = decisionKey,
            reason
        });
        await _notificationService.CreateAsync(
            submitterId.Value,
            NotificationType.ContentModerationResult,
            payload,
            cancellationToken);
    }
}
