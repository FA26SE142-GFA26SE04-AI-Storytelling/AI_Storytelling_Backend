using System.Globalization;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Stories.Services;

/// <summary>
/// Ảnh bìa có thể là một đường dẫn https thường, hoặc một tham chiếu tới ảnh minh họa của chính truyện:
/// <c>scene:{mediaAssetId}#crop=x,y,w,h</c>. Link ảnh minh họa được ký tạm nên không lưu thẳng vào DB.
/// </summary>
public static class StoryCover
{
    public const string ScenePrefix = "scene:";
    private const string CropMarker = "#crop=";

    public static bool IsSceneRef(string? value) =>
        value is not null && value.StartsWith(ScenePrefix, StringComparison.Ordinal);

    public static string Build(int mediaAssetId, double x, double y, double w, double h)
    {
        static string F(double n) => (Math.Round(n, 4)).ToString("0.####", CultureInfo.InvariantCulture);
        return $"{ScenePrefix}{mediaAssetId}{CropMarker}{F(x)},{F(y)},{F(w)},{F(h)}";
    }

    public static string Build(int mediaAssetId) => $"{ScenePrefix}{mediaAssetId}";

    /// <summary>Tách mã ảnh và phần vùng cắt (gồm cả "#crop=..." hoặc rỗng).</summary>
    public static bool TryParse(string? value, out int mediaAssetId, out string cropTail)
    {
        mediaAssetId = 0;
        cropTail = string.Empty;
        if (!IsSceneRef(value)) return false;
        var body = value![ScenePrefix.Length..];
        var cut = body.IndexOf(CropMarker, StringComparison.Ordinal);
        var idText = cut < 0 ? body : body[..cut];
        if (cut >= 0) cropTail = body[cut..];
        return int.TryParse(idText, NumberStyles.None, CultureInfo.InvariantCulture, out mediaAssetId) && mediaAssetId > 0;
    }
}

/// <summary>Đổi các tham chiếu ảnh bìa trong một loạt truyện thành link ảnh đã ký (tham chiếu hỏng → null).</summary>
public interface IStoryCoverResolver
{
    Task<Func<string?, string?>> ResolveAsync(IEnumerable<string?> covers, CancellationToken cancellationToken = default);
}

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
