using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCreatorMediaUploads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "media_assets",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "AiGenerated");

            migrationBuilder.AlterColumn<int>(
                name: "IllustrationBeatId",
                table: "media_asset_generation_tasks",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "media_asset_generation_tasks",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "GenerateImage");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Source",
                table: "media_assets");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "media_asset_generation_tasks");

            migrationBuilder.AlterColumn<int>(
                name: "IllustrationBeatId",
                table: "media_asset_generation_tasks",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
