using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace StoryPlatform.Infrastructure.AI;

internal readonly record struct ProviderTokenUsage(int? PromptTokens, int? CandidatesTokens, int? TotalTokens);

internal static partial class MediaProviderDiagnostics
{
    public const string ServiceName = "StoryPlatform.Core";

    private static readonly string[] AllowlistedRequestIdHeaders =
    [
        "x-goog-request-id",
        "x-request-id",
        "request-id",
        "x-guploader-uploadid"
    ];

    public static string? ExtractAllowlistedRequestId(HttpResponseMessage response)
    {
        foreach (var headerName in AllowlistedRequestIdHeaders)
        {
            if (!response.Headers.TryGetValues(headerName, out var values))
                continue;

            foreach (var raw in values)
            {
                var trimmed = raw?.Trim();
                if (!string.IsNullOrEmpty(trimmed) &&
                    SafeRequestIdPattern().IsMatch(trimmed) &&
                    !CredentialShapedRequestIdPattern().IsMatch(trimmed))
                {
                    return trimmed;
                }
            }
        }

        return null;
    }

    public static double? GetRetryAfterSeconds(HttpResponseMessage response)
    {
        var delta = response.Headers.RetryAfter?.Delta
            ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow);
        if (delta is null)
            return null;

        return Math.Round(Math.Max(0, delta.Value.TotalSeconds), 3);
    }

    public static TimeSpan? GetRetryAfterDelta(HttpResponseMessage response) =>
        response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow);

    public static ProviderTokenUsage ExtractUsageMetadata(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return default;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("usageMetadata", out var usage) ||
                usage.ValueKind != JsonValueKind.Object)
            {
                return default;
            }

            return new ProviderTokenUsage(
                TryReadInt(usage, "promptTokenCount"),
                TryReadInt(usage, "candidatesTokenCount"),
                TryReadInt(usage, "totalTokenCount"));
        }
        catch (JsonException)
        {
            return default;
        }
    }

    public static void LogStarted(
        ILogger logger,
        string operation,
        string provider,
        string model,
        int requestCharacters,
        int? requestBytes = null)
    {
        var (traceId, spanId) = CurrentTrace();
        logger.LogInformation(
            "Media provider request started Service={Service} Operation={Operation} Provider={Provider} Model={Model} TraceId={TraceId} SpanId={SpanId} RequestCharacters={RequestCharacters} RequestBytes={RequestBytes}",
            ServiceName, operation, provider, model, traceId, spanId, requestCharacters, requestBytes);
    }

    public static void LogCompleted(
        ILogger logger,
        string operation,
        string provider,
        string model,
        double durationMs,
        int httpStatus,
        string? providerRequestId,
        int? responseBytes,
        string? mimeType,
        ProviderTokenUsage usage = default)
    {
        var (traceId, spanId) = CurrentTrace();
        logger.LogInformation(
            "Media provider request completed Service={Service} Operation={Operation} Provider={Provider} Model={Model} TraceId={TraceId} SpanId={SpanId} DurationMs={DurationMs} HttpStatus={HttpStatus} ProviderRequestId={ProviderRequestId} ResponseBytes={ResponseBytes} MimeType={MimeType} PromptTokens={PromptTokens} CandidatesTokens={CandidatesTokens} TotalTokens={TotalTokens}",
            ServiceName, operation, provider, model, traceId, spanId, durationMs, httpStatus,
            providerRequestId, responseBytes, mimeType, usage.PromptTokens, usage.CandidatesTokens, usage.TotalTokens);
    }

    public static void LogCancelled(
        ILogger logger,
        string operation,
        string provider,
        string model,
        double durationMs,
        bool callerCancelled)
    {
        var (traceId, spanId) = CurrentTrace();
        logger.LogWarning(
            "Media provider request cancelled Service={Service} Operation={Operation} Provider={Provider} Model={Model} TraceId={TraceId} SpanId={SpanId} DurationMs={DurationMs} CancellationSource={CancellationSource}",
            ServiceName, operation, provider, model, traceId, spanId, durationMs,
            callerCancelled ? "Caller" : "HttpClientTimeout");
    }

    public static void LogFailed(
        ILogger logger,
        string operation,
        string provider,
        string model,
        double durationMs,
        int? httpStatus,
        string errorCode,
        double? retryAfterSeconds,
        string? providerRequestId,
        string errorType)
    {
        var (traceId, spanId) = CurrentTrace();
        logger.LogWarning(
            "Media provider request failed Service={Service} Operation={Operation} Provider={Provider} Model={Model} TraceId={TraceId} SpanId={SpanId} DurationMs={DurationMs} HttpStatus={HttpStatus} ErrorCode={ErrorCode} RetryAfterSeconds={RetryAfterSeconds} ProviderRequestId={ProviderRequestId} ErrorType={ErrorType}",
            ServiceName, operation, provider, model, traceId, spanId, durationMs, httpStatus,
            errorCode, retryAfterSeconds, providerRequestId, errorType);
    }

    private static int? TryReadInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var prop) &&
        prop.ValueKind == JsonValueKind.Number &&
        prop.TryGetInt32(out var value)
            ? value
            : null;

    private static (string? TraceId, string? SpanId) CurrentTrace()
    {
        var activity = Activity.Current;
        return activity is null ? (null, null) : (activity.TraceId.ToString(), activity.SpanId.ToString());
    }

    [GeneratedRegex(@"^[A-Za-z0-9._:/-]{1,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeRequestIdPattern();

    [GeneratedRegex(@"(?:eyJ[A-Za-z0-9_-]{6,}\.[A-Za-z0-9_-]{6,}\.[A-Za-z0-9_-]{6,}|AIza[0-9A-Za-z_-]{20,}|sk-[A-Za-z0-9_-]{16,}|ghp_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|AKIA[0-9A-Z]{16}|xox[baprs]-[A-Za-z0-9-]{10,}|sb_(?:secret|publishable)_[A-Za-z0-9_-]{16,}|bearer[:=_/-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex CredentialShapedRequestIdPattern();
}
