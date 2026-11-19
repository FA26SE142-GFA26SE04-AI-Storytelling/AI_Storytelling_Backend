namespace StoryPlatform.Infrastructure.Storage;

public sealed class S3StorageOptions
{
    public const string SectionName = "S3Storage";

    public string BucketName { get; set; } = string.Empty;
    public string Region { get; set; } = "ap-southeast-1";

    // Media is served to browsers only through CloudFront signed URLs (the bucket stays private).
    public string CdnDomain { get; set; } = string.Empty;
    public string CdnKeyPairId { get; set; } = string.Empty;
    public string CdnPrivateKey { get; set; } = string.Empty;
}
