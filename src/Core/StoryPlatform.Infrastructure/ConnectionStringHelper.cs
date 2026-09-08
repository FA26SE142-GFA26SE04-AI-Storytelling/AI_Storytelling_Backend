using System;
using Microsoft.Extensions.Configuration;

namespace StoryPlatform.Infrastructure;

/// <summary>
/// Lớp hỗ trợ đọc và thiết lập chuỗi kết nối cơ sở dữ liệu linh hoạt (từ Configuration hoặc Environment Variables).
/// </summary>
public static class ConnectionStringHelper
{
    public const string DefaultConnectionName = "DefaultConnection";

    public static string GetConnectionString(IConfiguration? configuration, string connectionName = DefaultConnectionName)
    {
        // 1. Đọc từ IConfiguration (appsettings.json hoặc secrets)
        var connectionString = configuration?.GetConnectionString(connectionName);

        // 2. Nếu không có, tìm trong biến môi trường hệ thống
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = Environment.GetEnvironmentVariable("DEFAULT_CONNECTION")
                               ?? Environment.GetEnvironmentVariable("DATABASE_URL");
        }

        // 3. Fallback mặc định cho môi trường phát triển local PostgreSQL
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = "Host=localhost;Port=5432;Database=ai_storytelling_db;Username=postgres;Password=postgres";
        }

        return connectionString;
    }
}
