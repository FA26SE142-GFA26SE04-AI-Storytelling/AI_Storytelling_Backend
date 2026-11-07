using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using StoryPlatform.Application.Features.Stories.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Stories.Services;

public sealed class StoryCoverResolver : IStoryCoverResolver
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IChildMediaUrlSigner _signer;

    public StoryCoverResolver(IUnitOfWork unitOfWork, IChildMediaUrlSigner signer)
    {
        _unitOfWork = unitOfWork;
        _signer = signer;
    }

    public async Task<Func<string?, string?>> ResolveAsync(IEnumerable<string?> covers, CancellationToken cancellationToken = default)
    {
        var refs = covers.Where(StoryCover.IsSceneRef).Select(value => value!).Distinct(StringComparer.Ordinal).ToList();
        if (refs.Count == 0) return value => value;

        var parsed = new Dictionary<string, (int AssetId, string Crop)>(StringComparer.Ordinal);
        foreach (var reference in refs)
            if (StoryCover.TryParse(reference, out var id, out var crop)) parsed[reference] = (id, crop);

        var ids = parsed.Values.Select(item => item.AssetId).Distinct().ToList();
        var assets = ids.Count == 0
            ? []
            : await _unitOfWork.Repository<MediaAsset>().FindAsync(
                asset => ids.Contains(asset.Id) && asset.Type == MediaType.Illustration
                         && asset.Status == MediaStatus.Ready && asset.Url != null,
                cancellationToken: cancellationToken);
        var pathById = assets.ToDictionary(asset => asset.Id, asset => asset.Url!);

        var url = await _signer.SignAsync(pathById.Values.Select(path => (string?)path), cancellationToken);
        var resolved = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (reference, item) in parsed)
        {
            var signed = pathById.TryGetValue(item.AssetId, out var path) ? url(path) : null;
            resolved[reference] = signed is null ? null : signed + item.Crop;
        }
        return value => StoryCover.IsSceneRef(value) ? resolved.GetValueOrDefault(value!) : value;
    }
}
