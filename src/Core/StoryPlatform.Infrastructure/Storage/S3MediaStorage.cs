using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using StoryPlatform.Application.Features.MediaStorage.Interfaces;
using StoryPlatform.Application.Features.MediaStorage.Models;

namespace StoryPlatform.Infrastructure.Storage;

/// <summary>
/// Private S3 bucket for media. The API reads and writes with its ECS task role; browsers get CloudFront
/// signed URLs, which (unlike S3 presigned URLs signed with temporary role credentials) stay valid for
/// their whole requested lifetime.
/// </summary>
public sealed class S3MediaStorage : IMediaStorage
{
    private readonly IAmazonS3 _s3;
    private readonly S3StorageOptions _options;
    private readonly ILogger<S3MediaStorage> _logger;
    private readonly Func<DateTime> _utcNow;

    public S3MediaStorage(IAmazonS3 s3, S3StorageOptions options, ILogger<S3MediaStorage> logger)
        : this(s3, options, logger, () => DateTime.UtcNow)
    {
    }

    internal S3MediaStorage(IAmazonS3 s3, S3StorageOptions options, ILogger<S3MediaStorage> logger, Func<DateTime> utcNow)
    {
        _s3 = s3;
        _options = options;
        _logger = logger;
        _utcNow = utcNow;
    }

    public async Task<string> UploadAsync(
        string storagePath,
        Stream content,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!content.CanRead) throw new ArgumentException("Media content must be readable.", nameof(content));
        if (!MediaObjectRules.AllowedMimeTypes.Contains(mimeType)) throw new ArgumentException("Unsupported media MIME type.", nameof(mimeType));

        var key = MediaObjectRules.NormalizePath(storagePath);
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        MediaObjectRules.ValidateContentMatchesMime(bytes, mimeType);

        using var body = new MemoryStream(bytes, writable: false);
        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = RequireBucket(),
            Key = key,
            InputStream = body,
            ContentType = mimeType,
            AutoCloseStream = false
        }, cancellationToken);

        _logger.LogInformation("Uploaded media object {StoragePath} to S3 bucket {Bucket}", key, _options.BucketName);
        return key;
    }

    public async Task<GeneratedMedia> ReadAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        var key = MediaObjectRules.NormalizePath(storagePath);
        using var response = await _s3.GetObjectAsync(RequireBucket(), key, cancellationToken);
        if (response.ContentLength > MediaObjectRules.MaxReadBytes) throw new InvalidOperationException("MEDIA_TOO_LARGE");

        var mime = response.Headers.ContentType ?? string.Empty;
        var separator = mime.IndexOf(';');
        if (separator >= 0) mime = mime[..separator].Trim();
        if (!MediaObjectRules.AllowedMimeTypes.Contains(mime)) throw new InvalidOperationException("UNSUPPORTED_MEDIA_TYPE");

        var bytes = await MediaObjectRules.ReadLimitedAsync(response.ResponseStream, cancellationToken);
        MediaObjectRules.ValidateContentMatchesMime(bytes, mime);
        return new(bytes, mime);
    }

    public Task<string> GetSignedUrlAsync(
        string storagePath,
        TimeSpan expiry,
        CancellationToken cancellationToken = default)
    {
        MediaObjectRules.ValidateSignedUrlExpiry(expiry);
        var key = MediaObjectRules.NormalizePath(storagePath);

        var domain = _options.CdnDomain?.Trim().TrimEnd('/') ?? string.Empty;
        if (domain.Length == 0 || domain.Contains("://", StringComparison.Ordinal) || domain.Contains('/') ||
            string.IsNullOrWhiteSpace(_options.CdnKeyPairId) || string.IsNullOrWhiteSpace(_options.CdnPrivateKey))
            throw new InvalidOperationException("S3_STORAGE_NOT_CONFIGURED");

        var url = $"https://{domain}/{MediaObjectRules.EncodePath(key)}";
        var expires = new DateTimeOffset(_utcNow().Add(expiry), TimeSpan.Zero).ToUnixTimeSeconds();
        return Task.FromResult(SignCannedUrl(url, expires));
    }

    // CloudFront canned policy: RSA-SHA1 over the policy JSON, URL-safe base64 ('+'->'-', '='->'_', '/'->'~').
    // https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/private-content-creating-signed-url-canned-policy.html
    private string SignCannedUrl(string url, long expiresEpochSeconds)
    {
        var epoch = expiresEpochSeconds.ToString(CultureInfo.InvariantCulture);
        var policy = "{\"Statement\":[{\"Resource\":\"" + url +
                     "\",\"Condition\":{\"DateLessThan\":{\"AWS:EpochTime\":" + epoch + "}}}]}";
        using var rsa = RSA.Create();
        rsa.ImportFromPem(NormalizePem(_options.CdnPrivateKey));
        var signature = rsa.SignData(Encoding.UTF8.GetBytes(policy), HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
        var encoded = Convert.ToBase64String(signature).Replace('+', '-').Replace('=', '_').Replace('/', '~');
        return $"{url}?Expires={epoch}&Signature={encoded}&Key-Pair-Id={Uri.EscapeDataString(_options.CdnKeyPairId.Trim())}";
    }

    public async Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        var key = MediaObjectRules.NormalizePath(storagePath);
        try
        {
            await _s3.DeleteObjectAsync(RequireBucket(), key, cancellationToken);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        _logger.LogInformation("Deleted media object {StoragePath} from S3 bucket {Bucket}", key, _options.BucketName);
    }

    private string RequireBucket() =>
        string.IsNullOrWhiteSpace(_options.BucketName)
            ? throw new InvalidOperationException("S3_STORAGE_NOT_CONFIGURED")
            : _options.BucketName;

    // Secrets Manager / env vars sometimes carry the PEM with literal "\n" instead of line breaks.
    private static string NormalizePem(string pem) =>
        pem.Contains('\n') ? pem : pem.Replace("\\n", "\n", StringComparison.Ordinal);
}
