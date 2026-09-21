namespace StoryPlatform.Infrastructure.AI;

/// <summary>
/// Options for Google Cloud Text-to-Speech V1Beta1 client.
/// Credentials are read lazily: if CredentialsFilePath is set, that file is used; otherwise
/// the provider falls back to Application Default Credentials (env GOOGLE_APPLICATION_CREDENTIALS).
/// </summary>
public sealed class TtsServiceOptions
{
    public const string SectionName = "AI:TTS";

    public string LanguageCode { get; set; } = "vi-VN";
    public string VoiceName { get; set; } = "vi-VN-Wavenet-A";
    public string AudioEncoding { get; set; } = "MP3";
    public double SpeakingRate { get; set; } = 1.0;
    public bool EnableWordTimings { get; set; } = true;
    public string? CredentialsFilePath { get; set; }
    public int TransportRetryCount { get; set; } = 3;
}
