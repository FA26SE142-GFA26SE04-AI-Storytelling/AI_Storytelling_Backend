using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Moderation.DTOs;
using StoryPlatform.Application.Features.Moderation.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Moderation.Services;

public class ModerationReviewViewService : IModerationReviewViewService
{
    public const string PublishedContentTab = "published_content";
    public const string RoadmapTemplatesTab = "roadmap_templates";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogWriter _auditLogWriter;

    public ModerationReviewViewService(IUnitOfWork unitOfWork, IAuditLogWriter auditLogWriter)
    {
        _unitOfWork = unitOfWork;
        _auditLogWriter = auditLogWriter;
    }

    public async Task<ModerationReviewViewDto> GrantAsync(
        int actorUserId, GrantReviewViewRequestDto request, CancellationToken cancellationToken = default)
    {
        var actor = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(actorUserId, cancellationToken);
        if (actor == null
            || actor.Status != AccountStatus.Active
            || actor.Role is not (UserRole.Moderator or UserRole.Administrator))
        {
            throw new ForbiddenException("Chỉ Moderator hoặc Administrator đang hoạt động mới được xem để rà soát.");
        }

        await ValidateTargetAsync(request, cancellationToken);

        var now = DateTime.UtcNow;
        var repository = _unitOfWork.Repository<ModerationReviewView>();
        var storyId = request.StoryId;
        var storyVersionId = request.StoryVersionId;
        var roadmapId = request.ReadingRoadmapId;
        var tab = request.SourceTab;

        // Quyền đang còn hạn thì dùng lại, không gia hạn: 2 giờ tính từ lần bấm đầu; hết hạn phải bấm lại.
        var view = await repository.FirstOrDefaultAsync(
            existing => existing.ModeratorUserId == actorUserId
                        && existing.SourceTab == tab
                        && existing.StoryId == storyId
                        && existing.StoryVersionId == storyVersionId
                        && existing.ReadingRoadmapId == roadmapId
                        && existing.ExpiresAt > now,
            cancellationToken: cancellationToken);

        if (view == null)
        {
            view = new ModerationReviewView
            {
                ModeratorUserId = actorUserId,
                StoryId = request.StoryId,
                StoryVersionId = request.StoryVersionId,
                ReadingRoadmapId = request.ReadingRoadmapId,
                SourceTab = request.SourceTab,
                GrantedAt = now,
                ExpiresAt = now.Add(ModerationQueuePolicy.ReviewViewLifetime)
            };
            await repository.AddAsync(view, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        // Mỗi lần bấm "Xem để rà soát" đều ghi audit, kể cả khi dùng lại quyền còn hạn.
        await _auditLogWriter.LogAsync(
            actorUserId, "ModerationReviewViewGranted", nameof(ModerationReviewView), view.Id,
            beforeState: null,
            afterState: new
            {
                sourceTab = view.SourceTab, storyId = view.StoryId, storyVersionId = view.StoryVersionId,
                readingRoadmapId = view.ReadingRoadmapId, expiresAt = view.ExpiresAt
            },
            cancellationToken: cancellationToken);

        return new ModerationReviewViewDto
        {
            Id = view.Id,
            SourceTab = view.SourceTab,
            StoryId = view.StoryId,
            StoryVersionId = view.StoryVersionId,
            ReadingRoadmapId = view.ReadingRoadmapId,
            GrantedAt = view.GrantedAt,
            ExpiresAt = view.ExpiresAt
        };
    }

    public async Task<bool> HasActiveViewAsync(
        int actorUserId, int? storyId, int? readingRoadmapId, CancellationToken cancellationToken = default)
    {
        // Quyền cũ không được dùng sau khi tài khoản bị vô hiệu hóa hoặc đổi vai trò.
        if (storyId.HasValue == readingRoadmapId.HasValue)
        {
            return false;
        }

        var actor = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(actorUserId, cancellationToken);
        if (actor == null || actor.IsDeleted || actor.Status != AccountStatus.Active
            || actor.Role is not (UserRole.Moderator or UserRole.Administrator))
        {
            return false;
        }

        var now = DateTime.UtcNow;
        return await _unitOfWork.Repository<ModerationReviewView>().ExistsAsync(
            view => view.ModeratorUserId == actorUserId
                    && view.StoryId == storyId
                    && view.ReadingRoadmapId == readingRoadmapId
                    && view.ExpiresAt > now,
            cancellationToken);
    }

    private async Task ValidateTargetAsync(GrantReviewViewRequestDto request, CancellationToken cancellationToken)
    {
        switch (request.SourceTab)
        {
            case PublishedContentTab:
                if (request.StoryId == null || request.ReadingRoadmapId != null)
                {
                    throw new BadRequestException("Tab nội dung đã công khai cần StoryId và không có ReadingRoadmapId.");
                }

                var story = await _unitOfWork.Repository<Story>().GetByIdAsync(request.StoryId.Value, cancellationToken)
                            ?? throw new NotFoundException("Truyện", request.StoryId.Value);
                if (!story.IsPublished)
                {
                    throw new BadRequestException("Chỉ xem để rà soát được nội dung đã công khai.");
                }

                if (request.StoryVersionId != null)
                {
                    var version = await _unitOfWork.Repository<StoryVersion>()
                        .GetByIdAsync(request.StoryVersionId.Value, cancellationToken);
                    if (version == null || version.StoryId != story.Id)
                    {
                        throw new BadRequestException("Phiên bản không thuộc truyện này.");
                    }
                }

                break;

            case RoadmapTemplatesTab:
                if (request.ReadingRoadmapId == null || request.StoryId != null || request.StoryVersionId != null)
                {
                    throw new BadRequestException("Tab bộ truyện mẫu chỉ cần ReadingRoadmapId.");
                }

                var roadmap = await _unitOfWork.Repository<ReadingRoadmap>()
                                  .GetByIdAsync(request.ReadingRoadmapId.Value, cancellationToken)
                              ?? throw new NotFoundException("Bộ truyện mẫu", request.ReadingRoadmapId.Value);
                if (roadmap.Status != RoadmapTemplateStatus.Published)
                {
                    throw new BadRequestException("Chỉ xem để rà soát được bộ truyện mẫu đang công khai.");
                }

                break;

            default:
                throw new BadRequestException("SourceTab phải là published_content hoặc roadmap_templates.");
        }
    }
}
