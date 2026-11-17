using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StageBasedChildRoadmap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_child_roadmap_items_ChildProfileId",
                table: "child_roadmap_items");

            // Đánh số chặng cho dữ liệu cũ: mỗi truyện cũ là một chặng. Completed theo thời điểm đọc xong → 1..c,
            // Open → c+1, Waiting theo thứ tự cũ → tiếp theo. Skipped/Removed giữ nguyên.
            migrationBuilder.Sql("""
                WITH live AS (
                    SELECT "Id", "ChildProfileId",
                           CASE "Status" WHEN 'Completed' THEN 0 WHEN 'Open' THEN 1 ELSE 2 END AS bucket,
                           COALESCE("CompletedAt", '-infinity'::timestamptz) AS done_at,
                           "Position" AS old_position
                    FROM child_roadmap_items
                    WHERE "Status" IN ('Completed', 'Open', 'Waiting') AND NOT "IsDeleted"
                ),
                numbered AS (
                    SELECT "Id",
                           ROW_NUMBER() OVER (
                               PARTITION BY "ChildProfileId"
                               ORDER BY bucket,
                                        CASE WHEN bucket = 0 THEN done_at END,
                                        CASE WHEN bucket = 2 THEN old_position END,
                                        "Id") AS stage
                    FROM live
                )
                UPDATE child_roadmap_items AS item
                SET "Position" = numbered.stage
                FROM numbered
                WHERE item."Id" = numbered."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Sẽ lỗi nếu lúc rollback có bé nhiều hơn một truyện Open. Không hoàn lại số Position.
            migrationBuilder.CreateIndex(
                name: "IX_child_roadmap_items_ChildProfileId",
                table: "child_roadmap_items",
                column: "ChildProfileId",
                unique: true,
                filter: "\"Status\" = 'Open' AND NOT \"IsDeleted\"");
        }
    }
}
