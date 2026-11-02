using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceStoryArchivedWithRejected : Migration
    {
        /// <summary>Gộp trạng thái truyện đã lưu trữ vào Rejected.</summary>
        public const string UpSql = """
            UPDATE stories SET "Status" = 'Rejected' WHERE "Status" = 'Archived';
            """;

        /// <summary>Chỉ khôi phục được truyện có metadata gỡ hoặc lý do lưu trữ.</summary>
        public const string DownSql = """
            UPDATE stories SET "Status" = 'Archived' WHERE "Status" = 'Rejected' AND ("ArchivedAt" IS NOT NULL OR "ArchivedReason" IS NOT NULL);
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(UpSql);
        }
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DownSql);
        }
    }
}
