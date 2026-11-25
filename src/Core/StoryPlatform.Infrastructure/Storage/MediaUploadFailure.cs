using StoryPlatform.Application.Features.MediaGeneration;

namespace StoryPlatform.Infrastructure.Storage;

internal static class MediaUploadFailure
{
    public static PermanentMediaGenerationException ForConfiguration() =>
        new("MEDIA_STORAGE_UPLOAD_NOT_CONFIGURED");

    // Keep remote bodies, SDK exception details and signed URLs out of persisted diagnostics.
    public static Exception ForStatus(int? status, TimeSpan? retryAfter = null) => status switch
    {
        401 or 403 => new PermanentMediaGenerationException("MEDIA_STORAGE_UPLOAD_ACCESS_DENIED"),
        404 => new PermanentMediaGenerationException("MEDIA_STORAGE_UPLOAD_BUCKET_NOT_FOUND"),
        408 => new TransientMediaGenerationException("MEDIA_STORAGE_UPLOAD_TIMEOUT", retryAfter: retryAfter),
        429 => new TransientMediaGenerationException("MEDIA_STORAGE_UPLOAD_RATE_LIMITED",
            retryAfter: retryAfter > TimeSpan.FromSeconds(15) ? retryAfter : TimeSpan.FromSeconds(15)),
        null or 0 or 423 or >= 500 => new TransientMediaGenerationException("MEDIA_STORAGE_UPLOAD_UNAVAILABLE", retryAfter: retryAfter),
        _ => new PermanentMediaGenerationException("MEDIA_STORAGE_UPLOAD_REJECTED")
    };
}
