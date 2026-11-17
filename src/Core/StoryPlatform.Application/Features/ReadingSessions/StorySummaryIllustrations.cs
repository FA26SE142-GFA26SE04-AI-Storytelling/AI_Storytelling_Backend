using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ReadingSessions;

/// <summary>
/// Tranh của cảnh đúng phạm vi màn đọc (ChildReadingService.GetContentAsync): truyện thường chỉ lấy tranh không thuộc gói;
/// gói gia đình lấy tranh của gói (lọc thêm manifest khi có). Kết quả xếp mới nhất trước.
/// </summary>
public static class StorySummaryIllustrations
{
    public static async Task<List<MediaAsset>> LoadAsync(IUnitOfWork uow, int? versionId, int? packageId,
        IReadOnlyCollection<int>? manifestAssetIds, IReadOnlyCollection<int> sceneIds, CancellationToken ct)
    {
        if (sceneIds.Count == 0) return [];
        var ids = sceneIds.ToList();
        var allowed = manifestAssetIds?.ToList();
        return (await uow.Repository<MediaAsset>().FindAsync(asset =>
                    asset.StoryVersionId == versionId && asset.StorySceneId.HasValue && ids.Contains(asset.StorySceneId.Value)
                    && asset.Type == MediaType.Illustration && asset.Status == MediaStatus.Ready && !asset.IsDeleted
                    && (allowed == null ? asset.ParentStoryPackageId == packageId : allowed.Contains(asset.Id)),
                cancellationToken: ct))
            .OrderByDescending(asset => asset.CompletedAt ?? asset.CreatedAt).ThenByDescending(asset => asset.Id)
            .ToList();
    }

    /// <summary>Tranh bé thấy ở một beat: bản IsCurrent trước, rồi mới nhất (giống SceneIllustrations của màn đọc).</summary>
    public static MediaAsset? ForBeat(IEnumerable<MediaAsset> assets, int beatId, bool preferCurrent = true) =>
        assets.Where(asset => asset.IllustrationBeatId == beatId && asset.Url != null)
            // Frozen package media are all current in the child reader's proxy projection.
            .OrderByDescending(asset => preferCurrent && asset.IsCurrent).FirstOrDefault();

    /// <summary>Tranh cả cảnh (như IllustrationUrl của màn đọc): tranh mới nhất của cảnh.</summary>
    public static MediaAsset? ForScene(IEnumerable<MediaAsset> assets, int sceneId)
    {
        var selected = assets.FirstOrDefault(asset => asset.StorySceneId == sceneId);
        return selected?.Url is null ? null : selected;
    }
}
