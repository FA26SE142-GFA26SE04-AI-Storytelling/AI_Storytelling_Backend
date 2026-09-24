namespace StoryPlatform.Infrastructure.AI;

/// <summary>
/// Options for Text-to-Speech service (Single TTS Mode: Gemini Expressive TTS).
/// Supports gemini-2.5-flash-tts via Vertex AI multimodal audio generation.
/// </summary>
public sealed class TtsServiceOptions
{
    public const string SectionName = "AI:TTS";

    public string Provider { get; set; } = "GeminiTTS";
    public string Model { get; set; } = "gemini-2.5-flash-tts";
    public string VoiceName { get; set; } = "Kore";
    public string LanguageCode { get; set; } = "vi-VN";
    public string AudioEncoding { get; set; } = "MP3";
    public double SpeakingRate { get; set; } = 1.0;
    public double Pitch { get; set; } = 0.0;
    public double VolumeGainDb { get; set; } = 0.0;
    public bool EnableWordTimings { get; set; } = false;
    public int MaxTextLength { get; set; } = 5000;
    public int TimeoutSeconds { get; set; } = 30;
    public int TransportRetryCount { get; set; } = 3;
    public int TransportRetryBaseDelayMs { get; set; } = 500;
    public string? CredentialsFilePath { get; set; }
}
