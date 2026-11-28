namespace StoryPlatform.Infrastructure.AI;

/// <summary>
/// Options for Text-to-Speech service (Single TTS Mode: Gemini Expressive TTS).
/// Supports gemini-2.5-flash-tts via Vertex AI multimodal audio generation.
/// </summary>
public sealed class TtsServiceOptions
{
    public string Fingerprint(string provider) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new
        { provider, Model, VoiceName, LanguageCode, AudioEncoding, SpeakingRate, Pitch, VolumeGainDb, EnableWordTimings }))));
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
    /// <summary>Gemini TTS không có mốc từng chữ: bật thì nghe lại audio bằng STT để lấy mốc (mặc định tắt).</summary>
    public bool AlignWordTimingsWithStt { get; set; } = false;
    /// <summary>Tỷ lệ chữ nguồn phải khớp chữ nghe được thì mới dùng mốc.</summary>
    public double MinAlignmentCoverage { get; set; } = 0.8;
    /// <summary>STT đồng bộ giới hạn ~1 phút: audio lớn hơn ngưỡng này (WAV 24 kHz 16-bit mono ≈ 58 giây) thì bỏ qua căn mốc.</summary>
    public int MaxAlignedAudioBytes { get; set; } = 2_800_000;
    public int MaxTextLength { get; set; } = 5000;
    public int TimeoutSeconds { get; set; } = 120;
    public int TransportRetryCount { get; set; } = 3;
    public int TransportRetryBaseDelayMs { get; set; } = 500;
    public string? CredentialsFilePath { get; set; }
}
