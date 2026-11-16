using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReadingSessionReadingMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReadingMode",
                table: "reading_sessions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReadingMode",
                table: "reading_sessions");
        }
    }
}
