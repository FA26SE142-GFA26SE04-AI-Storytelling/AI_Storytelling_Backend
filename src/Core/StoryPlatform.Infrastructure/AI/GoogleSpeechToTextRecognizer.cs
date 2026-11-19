using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;

namespace StoryPlatform.Infrastructure.AI;

/// <summary>
/// Google Cloud Speech-to-Text V2, recognize đồng bộ qua REST. Âm thanh chỉ nằm trong bộ nhớ;
/// không lưu, không ghi log âm thanh hay lời trẻ nói (khi lỗi chỉ log mã HTTP và lý do cấu hình Google trả về).
/// </summary>
public sealed class GoogleSpeechToTextRecognizer(
    HttpClient http, IOptions<SpeechToTextOptions> options, ILogger<GoogleSpeechToTextRecognizer> logger)
    : ISpeechRecognizer
{
    private readonly SpeechToTextOptions _options = options.Value;

    public async Task<IReadOnlyList<RecognizedWord>> RecognizeAsync(
        byte[] audio, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            config = new
            {
                autoDecodingConfig = new { },
                languageCodes = new[] { _options.LanguageCode },
                model = _options.Model,
                features = new { enableWordConfidence = true, enableWordTimeOffsets = true }
            },
            content = Convert.ToBase64String(audio)
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 1, 120)));
        try
        {
            using var response = await http.PostAsJsonAsync(RecognizeUri(_options), body, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                // Thân lỗi của Google chỉ có mã/lý do/mô tả cấu hình (vd. API chưa bật, thiếu quyền IAM), không chứa âm thanh hay lời trẻ.
                var (googleStatus, reason, message) = DescribeError(await SafeReadAsync(response, timeout.Token));
                logger.LogWarning(
                    "Speech-to-Text failed Status={Status} GoogleStatus={GoogleStatus} Reason={Reason} Message={Message}",
                    (int)response.StatusCode, googleStatus, reason, message);
                throw new SpeechRecognitionUnavailableException();
            }

            return ParseWords(await response.Content.ReadAsStringAsync(timeout.Token));
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning("Speech-to-Text unavailable Error={Error}", exception.GetType().Name);
            throw new SpeechRecognitionUnavailableException();
        }
    }

    private const int MaxLoggedMessageLength = 300;

    private static async Task<string> SafeReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try { return await response.Content.ReadAsStringAsync(cancellationToken); }
        catch (Exception exception) when (exception is HttpRequestException or IOException or OperationCanceledException) { return string.Empty; }
    }

    /// <summary>Tách error.status, ErrorInfo.reason và error.message (một dòng, tối đa 300 ký tự) từ thân lỗi Google API; không đọc được thì trả "unknown".</summary>
    internal static (string GoogleStatus, string Reason, string Message) DescribeError(string body)
    {
        const string unknown = "unknown";
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
                return (unknown, unknown, string.Empty);

            var status = error.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString()! : unknown;
            var reason = unknown;
            if (error.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    if (detail.ValueKind == JsonValueKind.Object && detail.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String)
                    {
                        reason = r.GetString()!;
                        break;
                    }
                }
            }

            var message = error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString()! : string.Empty;
            message = string.Join(' ', message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
            if (message.Length > MaxLoggedMessageLength) message = message[..MaxLoggedMessageLength];
            return (status, reason, message);
        }
        catch (JsonException)
        {
            return (unknown, unknown, string.Empty);
        }
    }

    internal static Uri RecognizeUri(SpeechToTextOptions options)
    {
        var host = string.Equals(options.Location, "global", StringComparison.OrdinalIgnoreCase)
            ? "speech.googleapis.com"
            : $"{options.Location}-speech.googleapis.com";
        return new Uri(
            $"https://{host}/v2/projects/{options.ProjectId}/locations/{options.Location}/recognizers/_:recognize");
    }

    internal static IReadOnlyList<RecognizedWord> ParseWords(string json)
    {
        var words = new List<RecognizedWord>();
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("results", out var results))
        {
            return words;
        }

        foreach (var result in results.EnumerateArray())
        {
            if (!result.TryGetProperty("alternatives", out var alternatives) || alternatives.GetArrayLength() == 0)
            {
                continue;
            }

            var best = alternatives[0];
            if (best.TryGetProperty("words", out var list) && list.GetArrayLength() > 0)
            {
                foreach (var word in list.EnumerateArray())
                {
                    var text = word.TryGetProperty("word", out var value) ? value.GetString() : null;
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    // Model không hỗ trợ độ tin cậy thì trả 0: coi như không biết.
                    double? confidence = word.TryGetProperty("confidence", out var c)
                                         && c.ValueKind == JsonValueKind.Number && c.GetDouble() > 0
                        ? c.GetDouble()
                        : null;
                    words.Add(new RecognizedWord(text, confidence));
                }
            }
            else if (best.TryGetProperty("transcript", out var transcript)
                     && !string.IsNullOrWhiteSpace(transcript.GetString()))
            {
                words.AddRange(transcript.GetString()!
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(text => new RecognizedWord(text, null)));
            }
        }

        return words;
    }
}
