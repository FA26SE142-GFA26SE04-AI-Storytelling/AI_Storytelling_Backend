namespace StoryPlatform.Infrastructure.AI;

public sealed class AIServiceOptions
{
    public const string SectionName = "AIService";

    public string BaseUrl { get; set; } = "http://localhost:5260";
    public string InternalApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gemini API Key - sử dụng khi kết nối trực tiếp không qua Vertex AI microservice
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Model sử dụng (default: gemini-3.8-flash)
    /// </summary>
    public string Model { get; set; } = "gemini-3.8-flash";

    /// <summary>
    /// Timeout cho mỗi request (giây)
    /// </summary>
    public int TimeoutSeconds { get; set; } = 150;
}
