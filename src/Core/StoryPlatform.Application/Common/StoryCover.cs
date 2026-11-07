using System.Globalization;

namespace StoryPlatform.Application.Common;

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
        static string F(double n) => Math.Round(n, 4).ToString("0.####", CultureInfo.InvariantCulture);
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
