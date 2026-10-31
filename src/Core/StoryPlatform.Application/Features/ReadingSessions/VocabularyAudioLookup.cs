using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ReadingSessions;

public static class VocabularyAudioLookup
{
    public static async Task<IReadOnlyDictionary<int, string>> FindUrlsAsync(
        IUnitOfWork unitOfWork, IReadOnlyCollection<int> vocabularyIds, CancellationToken cancellationToken = default)
    {
        if (vocabularyIds.Count == 0) return new Dictionary<int, string>();

        var ids = vocabularyIds.Distinct().ToList();
        var assets = await unitOfWork.Repository<MediaAsset>().FindAsync(
            asset => asset.Type == MediaType.VocabularyAudio
                     && asset.StoryVocabularyId != null
                     && ids.Contains(asset.StoryVocabularyId.Value)
                     && asset.Status == MediaStatus.Ready
                     && asset.IsCurrent
                     && !asset.IsDeleted
                     && asset.Url != null,
            cancellationToken: cancellationToken);
        return assets.GroupBy(asset => asset.StoryVocabularyId!.Value)
            .ToDictionary(group => group.Key,
                group => group.OrderByDescending(asset => asset.CompletedAt ?? asset.CreatedAt)
                    .ThenByDescending(asset => asset.Id).First().Url!);
    }
}
