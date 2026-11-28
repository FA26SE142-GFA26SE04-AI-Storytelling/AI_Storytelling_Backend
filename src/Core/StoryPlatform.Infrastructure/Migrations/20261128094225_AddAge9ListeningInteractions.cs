using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAge9ListeningInteractions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RelationsJson",
                table: "listening_interactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Counterpoint",
                table: "listening_interaction_options",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RelationsJson",
                table: "listening_interactions");

            migrationBuilder.DropColumn(
                name: "Counterpoint",
                table: "listening_interaction_options");
        }
    }
}
