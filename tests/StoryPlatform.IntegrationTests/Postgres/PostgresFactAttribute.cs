using Xunit;

namespace StoryPlatform.IntegrationTests.Postgres;

/// <summary>
/// Test chạy với PostgreSQL thật. Tự bỏ qua trừ khi biến môi trường STORYPLATFORM_TEST_PG chứa chuỗi kết nối tới một máy chủ
/// mà tài khoản đó có quyền CREATE/DROP DATABASE. Mỗi test dùng một database tạm và xóa nó khi xong.
/// </summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public const string EnvironmentVariable = "STORYPLATFORM_TEST_PG";

    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvironmentVariable)))
            Skip = $"Đặt biến môi trường {EnvironmentVariable} (chuỗi kết nối PostgreSQL có quyền CREATE/DROP DATABASE) để chạy test này.";
    }
}
