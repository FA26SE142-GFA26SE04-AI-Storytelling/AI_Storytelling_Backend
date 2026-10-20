using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.RoadmapTemplates.DTOs;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.RoadmapTemplates.Services;

public partial class RoadmapTemplateService
{
    public async Task<RoadmapCloneResultDto> CloneAsync(
        int actorUserId, int roadmapId, CancellationToken cancellationToken = default)
    {
        var actor = await RequireModeratorAsync(actorUserId, cancellationToken);
        var source = await _unitOfWork.Repository<ReadingRoadmap>().GetByIdAsync(roadmapId, cancellationToken);
        if (source == null || source.IsDeleted)
        {
            throw new NotFoundException("Bộ truyện mẫu", roadmapId);
        }

        var isPublishedSource = source.Status == RoadmapTemplateStatus.Published && source.ArchivedAt == null;
        var isOrphanDraft = source.Status == RoadmapTemplateStatus.Draft
                            && await IsInactiveCreatorAsync(source.CreatedByUserId, cancellationToken);
        if (!isPublishedSource && !isOrphanDraft)
        {
            throw new ConflictException(
                "Chỉ nhân bản được bộ đang công khai hoặc bản Nháp của Moderator đã bị vô hiệu hóa.");
        }

        var sourceId = source.Id;
        var openClones = await _unitOfWork.Repository<ReadingRoadmap>().FindAsync(
            clone => !clone.IsDeleted
                     && clone.ClonedFromId == sourceId
                     && (clone.Status == RoadmapTemplateStatus.Draft
                         || clone.Status == RoadmapTemplateStatus.PendingReview),
            cancellationToken: cancellationToken);
        var existing = openClones.OrderBy(clone => clone.Id).FirstOrDefault();
        if (existing != null)
        {
            return new RoadmapCloneResultDto
            {
                RoadmapId = existing.Id,
                SourceRoadmapId = source.Id,
                AlreadyExisted = true
            };
        }

        var sourceItems = (await _unitOfWork.Repository<ReadingRoadmapItem>().FindAsync(
                item => item.ReadingRoadmapId == sourceId, cancellationToken: cancellationToken))
            .OrderBy(item => item.Position)
            .ToList();
        var clone = new ReadingRoadmap
        {
            Name = source.Name,
            Description = source.Description,
            AgeBand = source.AgeBand,
            MainCategoryId = source.MainCategoryId,
            ReadingLevelMin = source.ReadingLevelMin,
            ReadingLevelMax = source.ReadingLevelMax,
            DefaultDaysPerStory = source.DefaultDaysPerStory,
            Status = RoadmapTemplateStatus.Draft,
            CreatedByUserId = actor.Id,
            ClonedFromId = source.Id
        };

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _unitOfWork.Repository<ReadingRoadmap>().AddAsync(clone, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (sourceItems.Count > 0)
            {
                await _unitOfWork.Repository<ReadingRoadmapItem>().AddRangeAsync(
                    sourceItems.Select(item => new ReadingRoadmapItem
                    {
                        ReadingRoadmapId = clone.Id,
                        StoryId = item.StoryId,
                        Position = item.Position,
                        Days = item.Days
                    }),
                    cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            await _auditLogWriter.LogAsync(
                actor.Id, "RoadmapTemplateCloned", nameof(ReadingRoadmap), clone.Id,
                beforeState: null,
                afterState: new { sourceRoadmapId = source.Id, storyCount = sourceItems.Count },
                cancellationToken: cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }

        return new RoadmapCloneResultDto
        {
            RoadmapId = clone.Id,
            SourceRoadmapId = source.Id,
            AlreadyExisted = false
        };
    }

    public async Task<RoadmapDetailDto> ArchiveAsync(
        int actorUserId,
        int roadmapId,
        ArchiveRoadmapRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var actor = await RequireModeratorAsync(actorUserId, cancellationToken);
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length < 3 || reason.Length > 500)
        {
            throw new BadRequestException("Lý do Archive bắt buộc, từ 3 đến 500 ký tự.");
        }

        var roadmap = await _unitOfWork.Repository<ReadingRoadmap>().GetByIdAsync(roadmapId, cancellationToken);
        if (roadmap == null || roadmap.IsDeleted)
        {
            throw new NotFoundException("Bộ truyện mẫu", roadmapId);
        }

        if (roadmap.Status != RoadmapTemplateStatus.Published || roadmap.ArchivedAt != null)
        {
            throw new ConflictException("Chỉ Archive được bộ đang công khai.");
        }

        roadmap.Status = RoadmapTemplateStatus.Archived;
        roadmap.ArchivedReason = RoadmapArchiveReason.Manual;
        roadmap.ArchivedAt = DateTime.UtcNow;
        roadmap.ArchivedByUserId = actor.Id;
        roadmap.ArchiveNote = reason;
        _unitOfWork.Repository<ReadingRoadmap>().Update(roadmap);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogWriter.LogAsync(
            actor.Id, "RoadmapTemplateArchived", nameof(ReadingRoadmap), roadmap.Id,
            beforeState: new { status = RoadmapTemplateStatus.Published.ToString() },
            afterState: new
            {
                status = RoadmapTemplateStatus.Archived.ToString(),
                reason = RoadmapArchiveReason.Manual.ToString(),
                note = reason
            },
            cancellationToken: cancellationToken);

        return await BuildDetailAsync(roadmap, actor, cancellationToken);
    }

    private async Task<bool> IsInactiveCreatorAsync(
        int creatorUserId, CancellationToken cancellationToken)
    {
        var creator = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(creatorUserId, cancellationToken);
        return creator == null || creator.IsDeleted || creator.Status != AccountStatus.Active;
    }
}
