namespace StoryPlatform.Application.Features.MediaStorage.Interfaces;

public interface IMediaStorage
{
    Task<StoryPlatform.Application.Features.MediaStorage.Models.GeneratedMedia> ReadAsync(string storagePath,
        CancellationToken cancellationToken = default) => throw new NotSupportedException("MEDIA_READ_NOT_SUPPORTED");
    Task<string> UploadAsync(
        string storagePath,
        Stream content,
        string mimeType,
        CancellationToken cancellationToken = default);

    Task<string> GetSignedUrlAsync(
        string storagePath,
        TimeSpan expiry,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default);
}
