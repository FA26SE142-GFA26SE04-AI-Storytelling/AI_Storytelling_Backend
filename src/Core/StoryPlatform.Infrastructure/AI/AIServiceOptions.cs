namespace StoryPlatform.Infrastructure.AI;

public sealed class AIServiceOptions
{
    public const string SectionName = "AIService";

    /// <summary>
    /// Gemini API Key - lấy từ https://aistudio.google.com
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Model Gemini sử dụng (default: gemini-2.5-flash)
    /// </summary>
    public string Model { get; set; } = "gemini-2.5-flash";

    /// <summary>
    /// Timeout cho mỗi request (giây)
    /// </summary>
    public int TimeoutSeconds { get; set; } = 150;
}
