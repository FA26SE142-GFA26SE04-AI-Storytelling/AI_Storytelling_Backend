namespace StoryPlatform.Infrastructure.Caching;

/// <summary>
/// Cấu hình cho Redis cache bên ngoài AWS (Upstash/Redis Cloud, ...) — đọc từ configuration
/// section "RedisSettings". ConnectionString là biến môi trường RedisSettings__ConnectionString
/// (production, tiêm qua ECS task secret) hoặc appsettings.Development.json (dev, đã bị
/// .gitignore chặn).
/// </summary>
public sealed class RedisOptions
{
    public const string SectionName = "RedisSettings";
    public string ConnectionString { get; set; } = string.Empty;
}
