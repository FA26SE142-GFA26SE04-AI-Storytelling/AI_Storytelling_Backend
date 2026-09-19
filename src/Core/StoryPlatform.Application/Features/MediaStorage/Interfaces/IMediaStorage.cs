namespace StoryPlatform.Application.Features.MediaStorage.Interfaces;

public interface IMediaStorage
{
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
