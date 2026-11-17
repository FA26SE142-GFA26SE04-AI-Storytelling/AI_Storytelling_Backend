using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRegenerationLimitsToPlanSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AudioRegenerationLimit",
                table: "plan_settings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ContentRegenerationLimit",
                table: "plan_settings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ImageRegenerationLimit",
                table: "plan_settings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OutlineRegenerationLimit",
                table: "plan_settings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "plan_settings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "AudioRegenerationLimit", "ContentRegenerationLimit", "ImageRegenerationLimit", "OutlineRegenerationLimit" },
                values: new object[] { 2, 2, 3, 3 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AudioRegenerationLimit",
                table: "plan_settings");

            migrationBuilder.DropColumn(
                name: "ContentRegenerationLimit",
                table: "plan_settings");

            migrationBuilder.DropColumn(
                name: "ImageRegenerationLimit",
                table: "plan_settings");

            migrationBuilder.DropColumn(
                name: "OutlineRegenerationLimit",
                table: "plan_settings");
        }
    }
}
