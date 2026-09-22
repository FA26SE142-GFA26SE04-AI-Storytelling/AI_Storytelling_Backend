using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Google.Cloud.TextToSpeech.V1Beta1;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StoryPlatform.Application.Features.MediaGeneration;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Application.Features.MediaStorage.Models;
using AudioEncoding = Google.Cloud.TextToSpeech.V1Beta1.AudioEncoding;
using TimepointType = Google.Cloud.TextToSpeech.V1Beta1.SynthesizeSpeechRequest.Types.TimepointType;

namespace StoryPlatform.Infrastructure.AI;

/// <summary>
/// Google Cloud Text-to-Speech V1Beta1 provider.
///
/// Uses <c>EnableTimePointing</c> (RepeatedField) to obtain word-level timing marks for karaoke
/// highlighting. The SSML tokenizer preserves whitespace, punctuation, and Vietnamese Unicode grapheme
/// clusters so that the original text and the audio stay byte-for-byte aligned.
///
/// Transport errors are retried internally; business-level retries live in MediaGenerationService.
/// </summary>
public sealed class GoogleCloudTtsProvider : ITtsProvider
{
    private readonly TextToSpeechClient _client;
    private readonly TtsServiceOptions _options;
    private readonly ILogger<GoogleCloudTtsProvider> _logger;

    public GoogleCloudTtsProvider(
        TextToSpeechClient client,
        IOptions<TtsServiceOptions> options,
        ILogger<GoogleCloudTtsProvider> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options.Value;
        _logger = logger;
    }

    public async Task<GeneratedMedia> GenerateAsync(string exactSceneText, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(exactSceneText))
            throw new PermanentMediaGenerationException("TTS_EMPTY_TEXT");

        var tokens = SsmlTokenizer.Tokenize(exactSceneText);
        var ssmlText = SsmlTokenizer.BuildSsmlWithMarks(tokens);
        var ssml = WrapSsml(ssmlText);

        var request = new SynthesizeSpeechRequest
        {
            Input = new SynthesisInput { Ssml = ssml },
            Voice = new VoiceSelectionParams
            {
                LanguageCode = _options.LanguageCode,
                Name = _options.VoiceName
            },
            AudioConfig = new AudioConfig
            {
                AudioEncoding = ParseAudioEncoding(_options.AudioEncoding),
                SpeakingRate = _options.SpeakingRate
            }
        };

        if (_options.EnableWordTimings)
        {
            request.EnableTimePointing.Add(TimepointType.SsmlMark);
        }

        var response = await _client.SynthesizeSpeechAsync(request, cancellationToken).ConfigureAwait(false);
        var bytes = response.AudioContent.ToByteArray();

        if (MagicByteValidators.ValidateAudio(bytes) is { IsValid: false } av)
            throw new PermanentMediaGenerationException($"TTS_VALIDATION_FAILED: {av.Reason}");

        var wordTimingsJson = _options.EnableWordTimings
            ? BuildWordTimingsJson(tokens, response)
            : null;

        return new GeneratedMedia(bytes, ResolveMimeType(_options.AudioEncoding), BuildMetadata(wordTimingsJson));
    }

    private string WrapSsml(string inner) => $"<speak>{inner}</speak>";

    private string? BuildWordTimingsJson(IReadOnlyList<TimingMark> tokens, SynthesizeSpeechResponse response)
    {
        // Build a map: mark name → start time from Cloud TTS.
        var timepointMap = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var tp in response.Timepoints)
        {
            if (!string.IsNullOrEmpty(tp.MarkName))
                timepointMap[tp.MarkName] = tp.TimeSeconds;
        }

        // Get the word tokens (those that had a <mark> injected).
        var wordTokens = tokens.Where(t => t.IsWord).ToList();
        var timings = new List<WordTimingJson>(wordTokens.Count);
        for (var i = 0; i < wordTokens.Count; i++)
        {
            var markName = $"w{wordTokens[i].Index}";
            var start = timepointMap.TryGetValue(markName, out var s) ? Math.Round(s, 3) : 0.0;
            double? end = null;
            // last word: end = null (Cloud TTS does not provide audio duration).
            if (i + 1 < wordTokens.Count)
            {
                var nextMarkName = $"w{wordTokens[i + 1].Index}";
                if (timepointMap.TryGetValue(nextMarkName, out var nextStart))
                    end = Math.Round(nextStart, 3);
            }
            timings.Add(new WordTimingJson(wordTokens[i].Text, start, end));
        }

        return JsonSerializer.Serialize(timings, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    private static string ResolveMimeType(string encoding) => encoding.ToUpperInvariant() switch
    {
        "MP3" => "audio/mpeg",
        "LINEAR16" => "audio/wav",
        "OGG_OPUS" => "audio/ogg",
        "MULAW" => "audio/basic",
        _ => "audio/mpeg"
    };

    private static AudioEncoding ParseAudioEncoding(string encoding) => encoding.ToUpperInvariant() switch
    {
        "MP3" => AudioEncoding.Mp3,
        "LINEAR16" => AudioEncoding.Linear16,
        "OGG_OPUS" => AudioEncoding.OggOpus,
        "MULAW" => AudioEncoding.Mulaw,
        "ALAW" => AudioEncoding.Alaw,
        _ => AudioEncoding.Mp3
    };

    private Dictionary<string, string> BuildMetadata(string? wordTimingsJson)
    {
        var meta = new Dictionary<string, string>
        {
            ["provider"] = "GoogleCloud",
            ["model"] = _options.VoiceName,
            ["languageCode"] = _options.LanguageCode
        };
        if (wordTimingsJson is not null)
            meta["wordTimingsJson"] = wordTimingsJson;
        return meta;
    }

    private sealed record WordTimingJson(string word, double start, double? end);
}
