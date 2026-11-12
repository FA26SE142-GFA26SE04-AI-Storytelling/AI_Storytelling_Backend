namespace StoryPlatform.Infrastructure.AI;

public sealed class AIServiceOptions
{
    public const string SectionName = "AIService";

    public string BaseUrl { get; set; } = "http://localhost:5260";
    public string InternalApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Audience của ID token Google khi AI API chạy trên Cloud Run có bật IAM (URL canonical của service).
    /// Để trống (local, Docker Compose) thì Core không gửi ID token.
    /// </summary>
    public string IdTokenAudience { get; set; } = string.Empty;

    /// <summary>
    /// Nội dung JSON khoá service account Google dùng để xin ID token. Chỉ hỗ trợ loại này: cấu hình
    /// workload identity federation không xin được ID token với thư viện Google hiện dùng.
    /// Để trống thì dùng Application Default Credentials. Là secret: chỉ nạp qua Secrets Manager / biến môi trường.
    /// </summary>
    public string GoogleCredentialsJson { get; set; } = string.Empty;

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
