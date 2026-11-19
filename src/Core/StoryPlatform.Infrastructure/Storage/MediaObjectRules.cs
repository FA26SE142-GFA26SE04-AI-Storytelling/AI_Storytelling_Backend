using StoryPlatform.Infrastructure.AI;

namespace StoryPlatform.Infrastructure.Storage;

/// <summary>Validation shared by every IMediaStorage backend (Supabase, S3).</summary>
internal static class MediaObjectRules
{
    public const int MaxReadBytes = 32 * 1024 * 1024;

    public static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp",
        "audio/mpeg", "audio/wav", "audio/ogg"
    };

    public static string NormalizePath(string storagePath)
    {
        if (string.IsNullOrWhiteSpace(storagePath)) throw new ArgumentException("Storage path is required.", nameof(storagePath));
        if (storagePath.Contains(':') || storagePath.StartsWith("//", StringComparison.Ordinal))
            throw new ArgumentException("Storage path must be a relative object key.", nameof(storagePath));
        var normalized = storagePath.Replace('\\', '/').Trim('/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".." || segment.Contains('?') || segment.Contains('#')))
            throw new ArgumentException("Storage path is invalid.", nameof(storagePath));
        return string.Join('/', segments);
    }

    public static string EncodePath(string path) =>
        string.Join('/', path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));

    public static void ValidateContentMatchesMime(byte[] bytes, string mimeType)
    {
        if (mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            var result = MagicByteValidators.ValidateImage(bytes);
            if (!result.IsValid || !string.Equals(result.DetectedMime, mimeType, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Media binary signature does not match expected image MIME '{mimeType}'. Detected: '{result.DetectedMime}'. Reason: {result.Reason}",
                    nameof(mimeType));
            }
        }
        else if (mimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
        {
            var result = MagicByteValidators.ValidateAudio(bytes);
            if (!result.IsValid || !string.Equals(result.DetectedMime, mimeType, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Media binary signature does not match expected audio MIME '{mimeType}'. Detected: '{result.DetectedMime}'. Reason: {result.Reason}",
                    nameof(mimeType));
            }
        }
    }

    public static void ValidateSignedUrlExpiry(TimeSpan expiry)
    {
        if (expiry <= TimeSpan.Zero || expiry > TimeSpan.FromDays(7))
            throw new ArgumentOutOfRangeException(nameof(expiry), "Signed URL expiry must be between 1 second and 7 days.");
    }

    public static async Task<byte[]> ReadLimitedAsync(Stream source, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(chunk, cancellationToken)) != 0)
        {
            if (buffer.Length + read > MaxReadBytes) throw new InvalidOperationException("MEDIA_TOO_LARGE");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}
