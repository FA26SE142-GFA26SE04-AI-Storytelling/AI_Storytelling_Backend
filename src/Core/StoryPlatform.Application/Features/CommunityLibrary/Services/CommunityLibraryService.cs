using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ChildProfiles.Safety;
using StoryPlatform.Application.Features.CommunityLibrary.Interfaces;
using StoryPlatform.Application.Features.RoadmapTemplates;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.CommunityLibrary.Services;

public class CommunityLibraryService : ICommunityLibraryService
{
    private readonly IUnitOfWork _unitOfWork;

    public CommunityLibraryService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<PagedResult<CommunityStoryDto>> ListAsync(
        int childProfileId, CommunityLibraryQueryDto query, CancellationToken cancellationToken = default)
    {
        var visible = await LoadVisibleAsync(childProfileId, cancellationToken);
        var pageIndex = Math.Max(query.PageIndex, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var rows = visible.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToList();
        return new PagedResult<CommunityStoryDto>(rows, visible.Count, pageIndex, pageSize);
    }

    public async Task<CommunityStoryDto> GetAsync(
        int childProfileId, int submissionId, CancellationToken cancellationToken = default)
    {
        var visible = await LoadVisibleAsync(childProfileId, cancellationToken);
        return visible.FirstOrDefault(row => row.SubmissionId == submissionId)
               ?? throw new NotFoundException("Truyện cộng đồng", submissionId);
    }

    /// <summary>
    /// Mọi truyện Community bé này được thấy, mới duyệt trước. Lọc trong bộ nhớ vì chính sách an toàn theo nhãn của
    /// từng bé không dịch được sang một câu truy vấn.
    /// </summary>
    private async Task<List<CommunityStoryDto>> LoadVisibleAsync(int childProfileId, CancellationToken cancellationToken)
    {
        var child = await _unitOfWork.Repository<ChildProfile>().GetByIdAsync(childProfileId, cancellationToken);
        if (child == null || child.IsDeleted || child.Status != ChildProfileStatus.Active)
        {
            throw new ForbiddenException("Hồ sơ này hiện không dùng được thư viện cộng đồng.");
        }

        var items = (await _unitOfWork.Repository<ModerationItem>().FindAsync(
                item => item.ItemType == ModerationItemType.CommunitySubmission
                        && item.Status == ModerationItemStatus.Approved
                        && !item.IsDeleted
                        && item.StoryId.HasValue
                        && item.StoryVersionId.HasValue,
                cancellationToken: cancellationToken))
            .OrderByDescending(item => item.DecidedAt ?? item.CreatedAt).ThenByDescending(item => item.Id).ToList();
        if (items.Count == 0)
        {
            return new List<CommunityStoryDto>();
        }

        var storyIds = items.Select(item => item.StoryId!.Value).Distinct().ToList();
        var versionIds = items.Select(item => item.StoryVersionId!.Value).Distinct().ToList();
        var stories = (await _unitOfWork.Repository<Story>().FindAsync(
            story => storyIds.Contains(story.Id), cancellationToken: cancellationToken)).ToDictionary(story => story.Id);
        var versions = (await _unitOfWork.Repository<StoryVersion>().FindAsync(
            version => versionIds.Contains(version.Id), cancellationToken: cancellationToken)).ToDictionary(version => version.Id);
        var links = await _unitOfWork.Repository<StoryCategory>().FindAsync(
            link => storyIds.Contains(link.StoryId), cancellationToken: cancellationToken);
        var categoryIds = links.Select(link => link.ContentCategoryId).Distinct().ToList();
        var categories = (await _unitOfWork.Repository<ContentCategory>().FindAsync(
            category => categoryIds.Contains(category.Id), cancellationToken: cancellationToken))
            .ToDictionary(category => category.Id);

        var childId = child.Id;
        var policy = await _unitOfWork.Repository<SafetyPolicy>().FirstOrDefaultAsync(
            existing => existing.ChildProfileId == childId && !existing.IsDeleted, cancellationToken: cancellationToken);
        IReadOnlyList<SafetyPolicyCategory> rules = new List<SafetyPolicyCategory>();
        if (policy != null)
        {
            var policyId = policy.Id;
            rules = await _unitOfWork.Repository<SafetyPolicyCategory>().FindAsync(
                rule => rule.SafetyPolicyId == policyId, cancellationToken: cancellationToken);
        }

        var ageText = RoadmapAge.ToText(child.AgeBand);
        var result = new List<CommunityStoryDto>();
        foreach (var item in items)
        {
            if (!stories.TryGetValue(item.StoryId!.Value, out var story)
                || story.IsDeleted
                || story.Status == StoryStatus.Archived
                || story.ArchivedAt != null
                || !versions.TryGetValue(item.StoryVersionId!.Value, out var version)
                || story.AgeBand != ageText)
            {
                continue;
            }

            var storyLinks = links.Where(link => link.StoryId == story.Id).ToList();
            if (storyLinks.Any(link => !SafetyPolicyEvaluator.IsCategoryPermitted(link.ContentCategoryId, rules)))
            {
                continue;
            }

            if (policy != null && policy.MaxStoryLength > 0 && (version.Content?.Length ?? 0) > policy.MaxStoryLength)
            {
                continue;
            }

            result.Add(new CommunityStoryDto
            {
                SubmissionId = item.Id,
                Title = version.Title,
                Description = story.Description,
                AgeBand = story.AgeBand,
                ReadingLevel = story.ReadingLevel,
                Labels = storyLinks
                    .Select(link => categories.GetValueOrDefault(link.ContentCategoryId))
                    .Where(category => category != null)
                    .Select(category => new CommunityLabelDto
                    {
                        Id = category!.Id, Code = category.Code, DisplayName = category.DisplayName
                    })
                    .OrderBy(label => label.Id).ToList(),
                Author = CommunityLibraryPolicy.AuthorName,
                ApprovedAt = item.DecidedAt
            });
        }

        return result;
    }
}
