using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.RoadmapTemplates.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.RoadmapTemplates.Services;

public class RoadmapSubmissionChecker : IRoadmapSubmissionChecker
{
    private readonly IUnitOfWork _unitOfWork;

    public RoadmapSubmissionChecker(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<List<RoadmapCheck>> EvaluateAsync(
        ReadingRoadmap roadmap,
        IReadOnlyList<ReadingRoadmapItem> items,
        CancellationToken cancellationToken = default)
    {
        var storyIds = items.Select(item => item.StoryId).Distinct().ToList();
        var stories = await _unitOfWork.Repository<Story>().FindAsync(
            story => storyIds.Contains(story.Id), cancellationToken: cancellationToken);
        var byId = stories.ToDictionary(story => story.Id);

        var checks = new List<RoadmapCheck>
        {
            items.Count > 0
                ? Pass(RoadmapCheckCodes.NotEmpty, "Bộ có truyện.")
                : Fail(RoadmapCheckCodes.NotEmpty, "Bộ phải có ít nhất một truyện.")
        };

        var notReady = items
            .Where(item => !byId.TryGetValue(item.StoryId, out var story) || !RoadmapEligibility.IsEligible(story))
            .Select(item => item.StoryId).ToList();
        checks.Add(notReady.Count == 0
            ? Pass(RoadmapCheckCodes.StoriesReady, "Mọi truyện là Public Library đang công khai.")
            : Fail(RoadmapCheckCodes.StoriesReady,
                $"Truyện không phải Public Library đang công khai: {Ids(notReady)}."));

        var ageText = RoadmapAge.ToText(roadmap.AgeBand);
        var wrongAge = items
            .Where(item => byId.TryGetValue(item.StoryId, out var story) && story.AgeBand != ageText)
            .Select(item => item.StoryId).ToList();
        checks.Add(wrongAge.Count == 0
            ? Pass(RoadmapCheckCodes.AgeMatch, "Độ tuổi từng truyện trùng độ tuổi của bộ.")
            : Fail(RoadmapCheckCodes.AgeMatch,
                $"Truyện lệch độ tuổi {ageText} của bộ: {Ids(wrongAge)}."));

        var wrongLevel = items
            .Where(item => byId.TryGetValue(item.StoryId, out var story)
                           && (!story.ReadingLevel.HasValue
                               || story.ReadingLevel < roadmap.ReadingLevelMin
                               || story.ReadingLevel > roadmap.ReadingLevelMax))
            .Select(item => item.StoryId).ToList();
        checks.Add(wrongLevel.Count == 0
            ? Pass(RoadmapCheckCodes.LevelInRange, "Reading level từng truyện nằm trong khoảng của bộ.")
            : Fail(RoadmapCheckCodes.LevelInRange,
                $"Truyện thiếu hoặc lệch reading level {roadmap.ReadingLevelMin}-{roadmap.ReadingLevelMax}: "
                + $"{Ids(wrongLevel)}."));

        var duplicated = items.GroupBy(item => item.StoryId).Where(group => group.Count() > 1)
            .Select(group => group.Key).ToList();
        checks.Add(duplicated.Count == 0
            ? Pass(RoadmapCheckCodes.NoDuplicateStories, "Không có truyện trùng.")
            : Fail(RoadmapCheckCodes.NoDuplicateStories, $"Truyện bị trùng trong bộ: {Ids(duplicated)}."));

        checks.Add(await CheckComboAsync(roadmap, cancellationToken));
        return checks;
    }

    private async Task<RoadmapCheck> CheckComboAsync(
        ReadingRoadmap roadmap, CancellationToken cancellationToken)
    {
        var roadmapId = roadmap.Id;
        var sourceId = roadmap.ClonedFromId ?? 0;
        var ageBand = roadmap.AgeBand;
        var categoryId = roadmap.MainCategoryId;
        var others = await _unitOfWork.Repository<ReadingRoadmap>().FindAsync(
            other => other.Id != roadmapId
                     && other.Id != sourceId
                     && !other.IsDeleted
                     && other.Status == RoadmapTemplateStatus.Published
                     && other.ArchivedAt == null
                     && other.AgeBand == ageBand
                     && other.MainCategoryId == categoryId,
            cancellationToken: cancellationToken);

        var existing = others.OrderBy(other => other.Id).FirstOrDefault();
        return existing == null
            ? Pass(RoadmapCheckCodes.ComboUnique, "Chưa có bộ công khai nào cùng độ tuổi và chủ đề chính.")
            : new RoadmapCheck(
                RoadmapCheckCodes.ComboUnique,
                false,
                $"Đã có bộ công khai cùng độ tuổi và chủ đề chính (bộ {existing.Id}). "
                + "Hãy Nhân bản bộ đó thay vì tạo bộ mới.",
                existing.Id);
    }

    private static RoadmapCheck Pass(string code, string message) => new(code, true, message);
    private static RoadmapCheck Fail(string code, string message) => new(code, false, message);
    private static string Ids(IEnumerable<int> ids) => string.Join(", ", ids.Distinct().OrderBy(id => id));
}
