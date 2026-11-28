using System.Text.Json;

namespace StoryPlatform.Application.Features.ListeningInteractions.Services;

/// <summary>A normalized image region; coordinates are relative to the rendered picture.</summary>
public sealed record HotspotBox(int AssetId, double X, double Y, double W, double H)
{
    public const double TouchPadding = 0.04;
    private const double MinArea = 0.002;

    public static HotspotBox? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("assetId", out var asset) || !root.TryGetProperty("x", out var x) ||
                !root.TryGetProperty("y", out var y) || !root.TryGetProperty("w", out var w) ||
                !root.TryGetProperty("h", out var h)) return null;
            var box = new HotspotBox(asset.GetInt32(), x.GetDouble(), y.GetDouble(), w.GetDouble(), h.GetDouble());
            return IsValid(box) ? box : null;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    public static bool IsValid(HotspotBox? box) => box is not null && box.AssetId > 0 &&
        double.IsFinite(box.X) && double.IsFinite(box.Y) && double.IsFinite(box.W) && double.IsFinite(box.H) &&
        box.X >= 0 && box.Y >= 0 && box.W > 0 && box.H > 0 &&
        box.X + box.W <= 1.0000001 && box.Y + box.H <= 1.0000001 && box.W * box.H >= MinArea;

    public bool Hits(double x, double y) => x is >= 0 and <= 1 && y is >= 0 and <= 1 &&
        x >= X - TouchPadding && x <= X + W + TouchPadding &&
        y >= Y - TouchPadding && y <= Y + H + TouchPadding;

    public string ToJson() => JsonSerializer.Serialize(new { assetId = AssetId, x = X, y = Y, w = W, h = H });
}
