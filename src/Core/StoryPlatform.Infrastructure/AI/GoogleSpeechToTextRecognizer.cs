using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;

namespace StoryPlatform.Infrastructure.AI;

/// <summary>
/// Google Cloud Speech-to-Text V2, recognize đồng bộ qua REST. Âm thanh chỉ nằm trong bộ nhớ;
/// không lưu, không ghi log âm thanh hay lời trẻ nói (chỉ log mã HTTP khi lỗi).
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
                logger.LogWarning("Speech-to-Text failed Status={Status}", (int)response.StatusCode);
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
