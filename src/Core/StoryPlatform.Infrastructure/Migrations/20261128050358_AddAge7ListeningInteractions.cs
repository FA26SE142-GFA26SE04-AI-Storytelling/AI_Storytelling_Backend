using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAge7ListeningInteractions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AcceptedOrdersJson",
                table: "listening_interactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HotspotJson",
                table: "listening_interactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetLabel",
                table: "listening_interactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HotspotJson",
                table: "listening_interaction_options",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "listening_interaction_options",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "None");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcceptedOrdersJson",
                table: "listening_interactions");

            migrationBuilder.DropColumn(
                name: "HotspotJson",
                table: "listening_interactions");

            migrationBuilder.DropColumn(
                name: "TargetLabel",
                table: "listening_interactions");

            migrationBuilder.DropColumn(
                name: "HotspotJson",
                table: "listening_interaction_options");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "listening_interaction_options");
        }
    }
}
