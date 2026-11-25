using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChildFeatureToggles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "LibraryBrowseEnabled",
                table: "safety_policies",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotebookEnabled",
                table: "safety_policies",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "QuizEnabled",
                table: "safety_policies",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ReflectionEnabled",
                table: "safety_policies",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "RewardsEnabled",
                table: "safety_policies",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LibraryBrowseEnabled",
                table: "safety_policies");

            migrationBuilder.DropColumn(
                name: "NotebookEnabled",
                table: "safety_policies");

            migrationBuilder.DropColumn(
                name: "QuizEnabled",
                table: "safety_policies");

            migrationBuilder.DropColumn(
                name: "ReflectionEnabled",
                table: "safety_policies");

            migrationBuilder.DropColumn(
                name: "RewardsEnabled",
                table: "safety_policies");
        }
    }
}
