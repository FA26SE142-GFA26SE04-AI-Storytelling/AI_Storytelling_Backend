using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using StoryPlatform.Application.Features.MediaStorage.Interfaces;

namespace StoryPlatform.Infrastructure.Storage;

public sealed partial class SupabaseMediaStorage : IMediaStorage
{
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
        if (!MediaObjectRules.AllowedMimeTypes.Contains(mimeType)) throw new ArgumentException("Unsupported media MIME type.", nameof(mimeType));

        var normalizedPath = MediaObjectRules.NormalizePath(storagePath);
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        MediaObjectRules.ValidateContentMatchesMime(bytes, mimeType);

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                BuildStorageUri($"object/{MediaObjectRules.EncodePath(_options.Bucket)}/{MediaObjectRules.EncodePath(normalizedPath)}"));
            request.Headers.TryAddWithoutValidation("x-upsert", "true");
            request.Content = new ByteArrayContent(bytes);
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType);

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                var error = await ReadStorageErrorAsync(response, cancellationToken);
                _logger.LogWarning(
                    "Supabase Storage upload failed Status={Status} ProviderStatus={ProviderStatus} ProviderCode={ProviderCode} Bucket={Bucket}",
                    status, error.Status, error.Code, _options.Bucket);
                var retry = response.Headers.RetryAfter;
                var retryAfter = retry?.Delta ?? (retry?.Date - DateTimeOffset.UtcNow);
                // Supabase Storage trả HTTP 400 cho cả "không có bucket" và "không có quyền", mã thật nằm trong body.
                var effective = status == 400 && error.Status is 401 or 403 or 404 ? error.Status.Value : status;
                throw MediaUploadFailure.ForStatus(effective, retryAfter);
            }
        }
        catch (InvalidOperationException ex) when (ex.Message == "SUPABASE_STORAGE_NOT_CONFIGURED")
        {
            throw MediaUploadFailure.ForConfiguration();
        }
        catch (HttpRequestException ex)
        {
            throw MediaUploadFailure.ForStatus((int?)ex.StatusCode);
        }
        _logger.LogInformation("Uploaded media object {StoragePath} to Supabase bucket {Bucket}",
            normalizedPath, _options.Bucket);
        return normalizedPath;
    }

    public async Task<StoryPlatform.Application.Features.MediaStorage.Models.GeneratedMedia> ReadAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        var normalized = MediaObjectRules.NormalizePath(storagePath);
        using var response = await _httpClient.GetAsync(BuildStorageUri($"object/authenticated/{MediaObjectRules.EncodePath(_options.Bucket)}/{MediaObjectRules.EncodePath(normalized)}"),
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        const int limit = 32 * 1024 * 1024;
        if (response.Content.Headers.ContentLength > limit) throw new InvalidOperationException("MEDIA_TOO_LARGE");
        var mime = response.Content.Headers.ContentType?.MediaType ?? "";
        if (!MediaObjectRules.AllowedMimeTypes.Contains(mime)) throw new InvalidOperationException("UNSUPPORTED_MEDIA_TYPE");
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(chunk, cancellationToken)) != 0)
        {
            if (buffer.Length + read > limit) throw new InvalidOperationException("MEDIA_TOO_LARGE");
            buffer.Write(chunk, 0, read);
        }
        var bytes = buffer.ToArray();
        MediaObjectRules.ValidateContentMatchesMime(bytes, mime);
        return new(bytes, mime);
    }

    public async Task<string> GetSignedUrlAsync(
        string storagePath,
        TimeSpan expiry,
        CancellationToken cancellationToken = default)
    {
        if (expiry <= TimeSpan.Zero || expiry > TimeSpan.FromDays(7))
            throw new ArgumentOutOfRangeException(nameof(expiry), "Signed URL expiry must be between 1 second and 7 days.");

        var normalizedPath = MediaObjectRules.NormalizePath(storagePath);
        using var response = await _httpClient.PostAsJsonAsync(
            BuildStorageUri($"object/sign/{MediaObjectRules.EncodePath(_options.Bucket)}/{MediaObjectRules.EncodePath(normalizedPath)}"),
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
        var normalizedPath = MediaObjectRules.NormalizePath(storagePath);
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            BuildStorageUri($"object/{MediaObjectRules.EncodePath(_options.Bucket)}"))
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

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        _ = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var status = (int)response.StatusCode;
        _logger.LogWarning(
            "Supabase Storage request failed Status={Status} Bucket={Bucket}",
            status, _options.Bucket);
        throw new HttpRequestException(
            $"Supabase Storage returned HTTP {status}.",
            null,
            response.StatusCode);
    }

    /// <summary>
    /// Mã lỗi Supabase Storage trong body: chỉ "statusCode" (số) và "code" khi đúng dạng tên mã (vd NoSuchBucket).
    /// Không đọc "error"/"message": các trường này có thể chứa đường dẫn object, URL ký sẵn hoặc khoá.
    /// </summary>
    private static async Task<StorageError> ReadStorageErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (body.Length > 4096) body = body[..4096];
            using var json = System.Text.Json.JsonDocument.Parse(body);
            var root = json.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object) return StorageError.None;
            int? status = root.TryGetProperty("statusCode", out var s)
                ? s.ValueKind == System.Text.Json.JsonValueKind.Number && s.TryGetInt32(out var n) ? n
                : s.ValueKind == System.Text.Json.JsonValueKind.String && int.TryParse(s.GetString(), out var p) ? p : null
                : null;
            var code = root.TryGetProperty("code", out var c) && c.ValueKind == System.Text.Json.JsonValueKind.String &&
                       ProviderCodePattern().IsMatch(c.GetString() ?? "") ? c.GetString() : null;
            return new StorageError(status is >= 100 and <= 599 ? status : null, code);
        }
        catch (System.Text.Json.JsonException)
        {
            return StorageError.None;
        }
    }

    [System.Text.RegularExpressions.GeneratedRegex("^[A-Z][A-Za-z]{2,40}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex ProviderCodePattern();

    private sealed record StorageError(int? Status, string? Code)
    {
        public static readonly StorageError None = new(null, null);
    }

    private sealed record SignedUrlResponse(
        [property: JsonPropertyName("signedURL")] string? SignedUrl);
}
