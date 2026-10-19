using System.Linq.Expressions;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.RoadmapTemplates.DTOs;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.RoadmapTemplates.Services;

public partial class RoadmapTemplateService
{
    public async Task<PagedResult<RoadmapListItemDto>> ListAsync(
        int actorUserId, RoadmapListQueryDto query, CancellationToken cancellationToken = default)
    {
        var actor = await RequireStaffAsync(actorUserId, cancellationToken);
        if (!Enum.IsDefined(query.Group))
        {
            throw new BadRequestException("Nhóm phải là Drafts, Pending, Published, Archived hoặc Orphaned.");
        }

        var pageIndex = Math.Max(query.PageIndex, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var actorId = actor.Id;

        var inactiveCreatorIds = new List<int>();
        if (query.Group == RoadmapListGroup.Orphaned)
        {
            var inactive = await _unitOfWork.Repository<UserAccount>().FindAsync(
                user => user.Role == UserRole.Moderator && user.Status != AccountStatus.Active,
                cancellationToken: cancellationToken);
            inactiveCreatorIds = inactive.Select(user => user.Id).ToList();
        }

        Expression<Func<ReadingRoadmap, bool>> filter = query.Group switch
        {
            RoadmapListGroup.Drafts => roadmap => !roadmap.IsDeleted
                                                  && roadmap.Status == RoadmapTemplateStatus.Draft
                                                  && roadmap.CreatedByUserId == actorId,
            RoadmapListGroup.Pending => roadmap => !roadmap.IsDeleted
                                                   && roadmap.Status == RoadmapTemplateStatus.PendingReview,
            RoadmapListGroup.Published => roadmap => !roadmap.IsDeleted
                                                     && roadmap.Status == RoadmapTemplateStatus.Published,
            RoadmapListGroup.Orphaned => roadmap => !roadmap.IsDeleted
                                                    && roadmap.Status == RoadmapTemplateStatus.Draft
                                                    && inactiveCreatorIds.Contains(roadmap.CreatedByUserId),
            _ => roadmap => !roadmap.IsDeleted && roadmap.Status == RoadmapTemplateStatus.Archived
        };
        Func<IQueryable<ReadingRoadmap>, IOrderedQueryable<ReadingRoadmap>> orderBy = query.Group switch
        {
            RoadmapListGroup.Published => source => source
                .OrderByDescending(roadmap => roadmap.PublishedAt).ThenByDescending(roadmap => roadmap.Id),
            RoadmapListGroup.Archived => source => source
                .OrderByDescending(roadmap => roadmap.ArchivedAt).ThenByDescending(roadmap => roadmap.Id),
            _ => source => source
                .OrderByDescending(roadmap => roadmap.CreatedAt).ThenByDescending(roadmap => roadmap.Id)
        };

        var (roadmaps, total) = await _unitOfWork.Repository<ReadingRoadmap>().GetPagedAsync(
            pageIndex, pageSize, filter, orderBy, cancellationToken: cancellationToken);
        var roadmapIds = roadmaps.Select(roadmap => roadmap.Id).ToList();
        var roadmapItems = await _unitOfWork.Repository<ReadingRoadmapItem>().FindAsync(
            item => roadmapIds.Contains(item.ReadingRoadmapId), cancellationToken: cancellationToken);
        var categoryIds = roadmaps.Select(roadmap => roadmap.MainCategoryId).Distinct().ToList();
        var categories = await _unitOfWork.Repository<ContentCategory>().FindAsync(
            category => categoryIds.Contains(category.Id), cancellationToken: cancellationToken);
        var creatorIds = roadmaps.Select(roadmap => roadmap.CreatedByUserId).Distinct().ToList();
        var creators = await _unitOfWork.Repository<UserAccount>().FindAsync(
            user => creatorIds.Contains(user.Id), cancellationToken: cancellationToken);

        var openCloneBySource = new Dictionary<int, int>();
        if (query.Group is RoadmapListGroup.Published or RoadmapListGroup.Orphaned)
        {
            var sourceIds = roadmaps.Select(roadmap => roadmap.Id).ToList();
            var openClones = await _unitOfWork.Repository<ReadingRoadmap>().FindAsync(
                clone => !clone.IsDeleted
                         && clone.ClonedFromId.HasValue
                         && sourceIds.Contains(clone.ClonedFromId.Value)
                         && (clone.Status == RoadmapTemplateStatus.Draft
                             || clone.Status == RoadmapTemplateStatus.PendingReview),
                cancellationToken: cancellationToken);
            foreach (var clone in openClones.OrderBy(candidate => candidate.Id))
            {
                openCloneBySource.TryAdd(clone.ClonedFromId!.Value, clone.Id);
            }
        }

        var items = roadmaps.Select(roadmap =>
        {
            var category = categories.FirstOrDefault(candidate => candidate.Id == roadmap.MainCategoryId);
            var creator = creators.FirstOrDefault(candidate => candidate.Id == roadmap.CreatedByUserId);
            return new RoadmapListItemDto
            {
                Id = roadmap.Id,
                Name = roadmap.Name,
                AgeBand = RoadmapAge.ToText(roadmap.AgeBand),
                MainCategory = category == null
                    ? null
                    : new RoadmapCategoryDto
                    {
                        Id = category.Id,
                        Code = category.Code,
                        DisplayName = category.DisplayName
                    },
                StoryCount = roadmapItems.Count(item => item.ReadingRoadmapId == roadmap.Id),
                Status = roadmap.Status.ToString(),
                CreatedByUserId = roadmap.CreatedByUserId,
                CreatedByName = creator == null ? null : DisplayName(creator),
                IsMine = roadmap.CreatedByUserId == actorId,
                SubmittedAt = roadmap.SubmittedAt,
                PublishedAt = roadmap.PublishedAt,
                OpenCloneId = openCloneBySource.TryGetValue(roadmap.Id, out var cloneId) ? cloneId : null
            };
        }).ToList();

        return new PagedResult<RoadmapListItemDto>(items, total, pageIndex, pageSize);
    }
}
