namespace StoryPlatform.Infrastructure.AI;

public sealed class AIServiceOptions
{
    public const string SectionName = "AIService";

    /// <summary>
    /// Base URL của StoryPlatform.AI.Api (default: http://localhost:5260)
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:5260";

    /// <summary>
    /// Header authentication X-Internal-Api-Key nếu có
    /// </summary>
    public string? InternalApiKey { get; set; }

    /// <summary>
    /// Gemini API Key - lấy từ https://aistudio.google.com (dự phòng)
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Model Gemini sử dụng (default: gemini-3.5-flash-lite)
    /// </summary>
    public string Model { get; set; } = "gemini-3.5-flash-lite";

    /// <summary>
    /// Timeout cho mỗi request (giây)
    /// </summary>
    public int TimeoutSeconds { get; set; } = 150;
}
