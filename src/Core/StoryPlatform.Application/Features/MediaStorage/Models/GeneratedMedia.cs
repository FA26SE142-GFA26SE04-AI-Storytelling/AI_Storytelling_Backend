namespace StoryPlatform.Application.Features.MediaStorage.Models;

public sealed record GeneratedMedia(
    byte[] Content,
    string MimeType,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public Stream OpenReadStream() => new MemoryStream(Content, writable: false);

    public string SuggestedExtension => MimeType.ToLowerInvariant() switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/webp" => ".webp",
        "audio/mpeg" => ".mp3",
        "audio/wav" => ".wav",
        "audio/ogg" => ".ogg",
        _ => throw new InvalidOperationException("UNSUPPORTED_MEDIA_TYPE")
    };

    public string? GetMetadata(string key) =>
        Metadata is not null && Metadata.TryGetValue(key, out var value) ? value : null;
}
