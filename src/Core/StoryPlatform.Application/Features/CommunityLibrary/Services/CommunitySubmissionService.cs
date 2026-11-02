using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Application.Features.CommunityLibrary.Interfaces;
using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Application.Features.Moderation.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.CommunityLibrary.Services;

public partial class CommunitySubmissionService : ICommunitySubmissionService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IChildProfileAccessGuard _guard;
    private readonly IModerationQueueService _queueService;
    private readonly IAuditLogWriter _auditLogWriter;

    public CommunitySubmissionService(
        IUnitOfWork unitOfWork,
        IChildProfileAccessGuard guard,
        IModerationQueueService queueService,
        IAuditLogWriter auditLogWriter)
    {
        _unitOfWork = unitOfWork;
        _guard = guard;
        _queueService = queueService;
        _auditLogWriter = auditLogWriter;
    }

    private sealed record Evaluation(
        CommunityEligibilityDto Dto, StoryVersion? Version, ChildProfile? Child, CopyrightDeclaration? Declaration);

    public async Task<CommunityEligibilityDto> CheckAsync(
        int actorUserId, CommunitySubmitRequestDto request, CancellationToken cancellationToken = default)
    {
        var actor = await RequireParentAsync(actorUserId, cancellationToken);
        var story = await LoadStoryAsync(request.StoryId, cancellationToken);
        return (await EvaluateAsync(story, actor, request, cancellationToken)).Dto;
    }

    private async Task<Story> LoadStoryAsync(int storyId, CancellationToken cancellationToken)
    {
        var story = await _unitOfWork.Repository<Story>().GetByIdAsync(storyId, cancellationToken);
        if (story == null || story.IsDeleted)
        {
            throw new NotFoundException("Truyện", storyId);
        }

        return story;
    }

    private async Task<UserAccount> RequireParentAsync(int userId, CancellationToken cancellationToken)
    {
        var user = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(userId, cancellationToken);
        if (user == null || user.IsDeleted || user.Status != AccountStatus.Active || user.Role != UserRole.User)
        {
            throw new ForbiddenException("Chỉ phụ huynh đang hoạt động mới nộp được truyện lên Community.");
        }

        return user;
    }

    /// <summary>Đánh giá mọi điều kiện nộp; quyền với bé nguồn được kiểm ở đây (ném 403 nếu thiếu).</summary>
    private async Task<Evaluation> EvaluateAsync(
        Story story, UserAccount actor, CommunitySubmitRequestDto request, CancellationToken cancellationToken)
    {
        var dto = new CommunityEligibilityDto { StoryId = story.Id };

        if (story.Scope != StoryScope.Parent || !story.ChildProfileId.HasValue || ParentStoryContext.IsEditorStory(story))
        {
            AddBlocker(dto, CommunityBlockerCodes.StoryNotPrivate,
                "Chỉ nộp được truyện riêng của bé; truyện Public Library không nộp Community.");
            return new Evaluation(Finish(dto), null, null, null);
        }

        var childId = story.ChildProfileId.Value;
        await _guard.EnsureOwnerAsync(childId, actor.Id, cancellationToken);
        var child = await _unitOfWork.Repository<ChildProfile>().GetByIdAsync(childId, cancellationToken);

        if (story.Status == StoryStatus.Rejected || story.ArchivedAt != null)
        {
            AddBlocker(dto, CommunityBlockerCodes.StoryArchived, "Truyện đã bị lưu trữ nên không nộp được.");
        }
        else if (story.Status != StoryStatus.Ready)
        {
            AddBlocker(dto, CommunityBlockerCodes.StoryNotReady, "Truyện chưa sẵn sàng (chưa tạo xong media).");
        }

        var versions = await _unitOfWork.Repository<StoryVersion>().FindAsync(
            version => version.StoryId == story.Id, cancellationToken: cancellationToken);
        var current = versions.FirstOrDefault(version => version.IsCurrent);
        if (current == null)
        {
            AddBlocker(dto, CommunityBlockerCodes.NoCurrentVersion, "Truyện chưa có phiên bản hiện hành để nộp.");
        }
        else
        {
            dto.StoryVersionId = current.Id;
        }

        var storyId = story.Id;
        var hasOpenReport = await _unitOfWork.Repository<ContentReport>().ExistsAsync(
            report => report.StoryId == storyId
                      && !report.IsDeleted
                      && (report.Status == ContentReportStatus.Pending || report.Status == ContentReportStatus.Escalated),
            cancellationToken);
        if (hasOpenReport)
        {
            AddBlocker(dto, CommunityBlockerCodes.OpenReport,
                "Truyện đang có báo cáo chờ xử lý nên chưa nộp được.");
        }

        var alreadySubmitted = await _unitOfWork.Repository<ModerationItem>().ExistsAsync(
            item => item.ItemType == ModerationItemType.CommunitySubmission
                    && item.StoryId == storyId
                    && !item.IsDeleted
                    && (item.Status == ModerationItemStatus.Pending
                        || item.Status == ModerationItemStatus.Claimed
                        || item.Status == ModerationItemStatus.Approved),
            cancellationToken);
        if (alreadySubmitted)
        {
            AddBlocker(dto, CommunityBlockerCodes.AlreadySubmitted,
                "Truyện này đã được nộp và đang chờ hoặc đã có trong thư viện cộng đồng.");
        }

        var declaration = EvaluateCopyright(story, request.CopyrightDeclaration, dto);

        if (current != null)
        {
            var findings = await CommunityPiiScanner.ScanAsync(
                _unitOfWork, story, current, child?.Nickname, cancellationToken);
            dto.PiiFindings = findings.Select(finding => new PiiFindingDto
            {
                Kind = finding.Kind, Location = finding.Location, Excerpt = finding.Excerpt
            }).ToList();
            if (findings.Count > 0)
            {
                AddBlocker(dto, CommunityBlockerCodes.PiiFound,
                    "Truyện còn thông tin có thể nhận ra bé hoặc gia đình (số điện thoại, email, địa chỉ, tên trường, "
                    + "biệt danh…). Hãy sửa thành phiên bản mới rồi nộp lại.");
            }
        }

        return new Evaluation(Finish(dto), current, child, declaration);
    }

    /// <summary>BR-L4F-04: truyện AI nền tảng sinh tự đạt; truyện tự viết / nhập phải khai OwnWork.</summary>
    private static CopyrightDeclaration? EvaluateCopyright(
        Story story, CopyrightDeclaration? declared, CommunityEligibilityDto dto)
    {
        if (story.Source == StorySource.Ai)
        {
            dto.RequiredDeclaration = "none";
            return CopyrightDeclaration.AiGenerated;
        }

        dto.RequiredDeclaration = "own_work";
        if (declared == CopyrightDeclaration.OwnWork)
        {
            return CopyrightDeclaration.OwnWork;
        }

        if (declared == CopyrightDeclaration.FamilyUse)
        {
            AddBlocker(dto, CommunityBlockerCodes.FamilyUse,
                "Truyện chỉ cam kết dùng trong gia đình nên không nộp lên Community được.");
        }
        else
        {
            AddBlocker(dto, CommunityBlockerCodes.CopyrightRequired,
                "Hãy xác nhận \"Đây là tác phẩm do tôi sáng tác\" để nộp truyện này.");
        }

        return null;
    }

    private static void AddBlocker(CommunityEligibilityDto dto, string code, string message) =>
        dto.Blockers.Add(new CommunityBlockerDto { Code = code, Message = message });

    private static CommunityEligibilityDto Finish(CommunityEligibilityDto dto)
    {
        dto.CanSubmit = dto.Blockers.Count == 0;
        return dto;
    }
}
