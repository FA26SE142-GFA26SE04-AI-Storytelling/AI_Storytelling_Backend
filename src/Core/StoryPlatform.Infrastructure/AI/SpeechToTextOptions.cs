namespace StoryPlatform.Infrastructure.AI;

/// <summary>Google Cloud Speech-to-Text V2 cho luyện đọc (Luồng 3 mục 4).</summary>
public sealed class SpeechToTextOptions
{
    public const string SectionName = "AI:STT";

    /// <summary>Để trống thì dùng AI:Vertex:ProjectId.</summary>
    public string ProjectId { get; set; } = string.Empty;
    public string Location { get; set; } = "global";
    public string Model { get; set; } = "long";
    public string LanguageCode { get; set; } = "vi-VN";
    public int TimeoutSeconds { get; set; } = 30;
}
