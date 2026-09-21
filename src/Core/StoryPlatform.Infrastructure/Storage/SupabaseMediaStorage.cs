using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using StoryPlatform.Application.Features.MediaStorage.Interfaces;
using StoryPlatform.Infrastructure.AI;

namespace StoryPlatform.Infrastructure.Storage;

public sealed class SupabaseMediaStorage : IMediaStorage
{
    private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp",
        "audio/mpeg", "audio/wav", "audio/ogg"
    };

    private readonly HttpClient _httpClient;
    private readonly SupabaseStorageOptions _options;
    private readonly ILogger<SupabaseMediaStorage> _logger;

    public SupabaseMediaStorage(
        HttpClient httpClient,
        SupabaseStorageOptions options,
        ILogger<SupabaseMediaStorage> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<string> UploadAsync(
        string storagePath,
        Stream content,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!content.CanRead) throw new ArgumentException("Media content must be readable.", nameof(content));
        if (!AllowedMimeTypes.Contains(mimeType)) throw new ArgumentException("Unsupported media MIME type.", nameof(mimeType));

        var normalizedPath = NormalizePath(storagePath);
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        ValidateContentMatchesMime(bytes, mimeType);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildStorageUri($"object/{EncodePath(_options.Bucket)}/{EncodePath(normalizedPath)}"));
        request.Headers.TryAddWithoutValidation("x-upsert", "true");
        request.Content = new ByteArrayContent(bytes);
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        _logger.LogInformation("Uploaded media object {StoragePath} to Supabase bucket {Bucket}",
            normalizedPath, _options.Bucket);
        return normalizedPath;
    }

    private static void ValidateContentMatchesMime(byte[] bytes, string mimeType)
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

    public async Task<string> GetSignedUrlAsync(
        string storagePath,
        TimeSpan expiry,
        CancellationToken cancellationToken = default)
    {
        if (expiry <= TimeSpan.Zero || expiry > TimeSpan.FromDays(7))
            throw new ArgumentOutOfRangeException(nameof(expiry), "Signed URL expiry must be between 1 second and 7 days.");

        var normalizedPath = NormalizePath(storagePath);
        using var response = await _httpClient.PostAsJsonAsync(
            BuildStorageUri($"object/sign/{EncodePath(_options.Bucket)}/{EncodePath(normalizedPath)}"),
            new { expiresIn = checked((int)Math.Ceiling(expiry.TotalSeconds)) },
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<SignedUrlResponse>(cancellationToken)
                     ?? throw new InvalidOperationException("SUPABASE_SIGNED_URL_INVALID_RESPONSE");
        if (string.IsNullOrWhiteSpace(result.SignedUrl))
            throw new InvalidOperationException("SUPABASE_SIGNED_URL_INVALID_RESPONSE");

        return BuildSignedUrl(result.SignedUrl);
    }

    public async Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        var normalizedPath = NormalizePath(storagePath);
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            BuildStorageUri($"object/{EncodePath(_options.Bucket)}"))
        {
            Content = JsonContent.Create(new { prefixes = new[] { normalizedPath } })
        };
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return;

        await EnsureSuccessAsync(response, cancellationToken);
        _logger.LogInformation("Deleted media object {StoragePath} from Supabase bucket {Bucket}",
            normalizedPath, _options.Bucket);
    }

    private Uri BuildStorageUri(string relativePath) =>
        new(GetProjectBaseUri(), $"storage/v1/{relativePath}");

    private string BuildSignedUrl(string signedUrl)
    {
        if (Uri.TryCreate(signedUrl, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            return absolute.AbsoluteUri;
        }

        var relative = signedUrl.TrimStart('/');
        if (relative.StartsWith("storage/v1/", StringComparison.OrdinalIgnoreCase))
            return new Uri(GetProjectBaseUri(), relative).AbsoluteUri;
        return new Uri(GetProjectBaseUri(), $"storage/v1/{relative}").AbsoluteUri;
    }

    private Uri GetProjectBaseUri()
    {
        if (!Uri.TryCreate(_options.Url?.TrimEnd('/') + "/", UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback))
            throw new InvalidOperationException("SUPABASE_STORAGE_NOT_CONFIGURED");
        if (string.IsNullOrWhiteSpace(_options.Bucket))
            throw new InvalidOperationException("SUPABASE_STORAGE_NOT_CONFIGURED");
        return uri;
    }

    private static string NormalizePath(string storagePath)
    {
        if (string.IsNullOrWhiteSpace(storagePath)) throw new ArgumentException("Storage path is required.", nameof(storagePath));
        var normalized = storagePath.Replace('\\', '/').Trim('/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".." || segment.Contains('?') || segment.Contains('#')))
            throw new ArgumentException("Storage path is invalid.", nameof(storagePath));
        return string.Join('/', segments);
    }

    private static string EncodePath(string path) =>
        string.Join('/', path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(
            $"Supabase Storage returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). " +
            $"Response: {Truncate(body, 512)}",
            null,
            response.StatusCode);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private sealed record SignedUrlResponse(
        [property: JsonPropertyName("signedURL")] string? SignedUrl);
}
