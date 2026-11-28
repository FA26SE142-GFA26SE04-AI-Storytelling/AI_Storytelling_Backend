using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.ReadingSessions;

namespace StoryPlatform.Infrastructure.AI;

/// <summary>
/// Nghe lại audio đã tạo bằng Google Cloud Speech-to-Text V2 (recognize đồng bộ qua REST) và trả mốc từng chữ.
/// Âm thanh chỉ nằm trong bộ nhớ; khi lỗi chỉ log mã HTTP và lý do cấu hình Google trả về, không log âm thanh hay văn bản.
/// </summary>
public sealed class GoogleSpeechWordAligner(
    HttpClient http, IOptions<SpeechToTextOptions> options, ILogger<GoogleSpeechWordAligner> logger)
    : IAudioWordAligner
{
    private readonly SpeechToTextOptions _options = options.Value;

    public async Task<IReadOnlyList<TimedWord>> RecognizeWordsAsync(byte[] audio, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            config = new
            {
                autoDecodingConfig = new { },
                languageCodes = new[] { _options.LanguageCode },
                model = _options.Model,
                features = new { enableWordTimeOffsets = true }
            },
            content = Convert.ToBase64String(audio)
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 1, 120)));
        try
        {
            using var response = await http.PostAsJsonAsync(GoogleSpeechToTextRecognizer.RecognizeUri(_options), body, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                var (googleStatus, reason, _) = GoogleSpeechToTextRecognizer.DescribeError(await SafeReadAsync(response, timeout.Token));
                logger.LogWarning("TTS word alignment STT failed Status={Status} GoogleStatus={GoogleStatus} Reason={Reason}",
                    (int)response.StatusCode, googleStatus, reason);
                throw new SpeechRecognitionUnavailableException();
            }
            return ParseTimedWords(await response.Content.ReadAsStringAsync(timeout.Token));
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning("TTS word alignment STT unavailable Error={Error}", exception.GetType().Name);
            throw new SpeechRecognitionUnavailableException();
        }
    }

    private static async Task<string> SafeReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try { return await response.Content.ReadAsStringAsync(ct); }
        catch (Exception exception) when (exception is HttpRequestException or IOException or OperationCanceledException) { return string.Empty; }
    }

    internal static IReadOnlyList<TimedWord> ParseTimedWords(string json)
    {
        var words = new List<TimedWord>();
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("results", out var results)) return words;

        foreach (var result in results.EnumerateArray())
        {
            if (!result.TryGetProperty("alternatives", out var alternatives) || alternatives.GetArrayLength() == 0) continue;
            if (!alternatives[0].TryGetProperty("words", out var list)) continue;
            foreach (var word in list.EnumerateArray())
            {
                var text = word.TryGetProperty("word", out var value) ? value.GetString() : null;
                if (string.IsNullOrWhiteSpace(text)) continue;
                words.Add(new TimedWord(text,
                    ParseOffsetSeconds(word.TryGetProperty("startOffset", out var s) ? s.GetString() : null),
                    ParseOffsetSeconds(word.TryGetProperty("endOffset", out var e) ? e.GetString() : null)));
            }
        }
        return words;
    }

    /// <summary>Duration của proto JSON có dạng "1.200s".</summary>
    internal static double ParseOffsetSeconds(string? offset)
    {
        if (string.IsNullOrWhiteSpace(offset) || !offset.EndsWith('s')) return 0;
        return double.TryParse(offset[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ? seconds : 0;
    }
}
