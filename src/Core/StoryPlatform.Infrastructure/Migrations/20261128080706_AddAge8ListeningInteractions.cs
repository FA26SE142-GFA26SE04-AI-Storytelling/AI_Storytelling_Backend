using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAge8ListeningInteractions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_read_aloud_recordings_session_practice",
                table: "read_aloud_recordings");

            migrationBuilder.AddColumn<int>(
                name: "ListeningInteractionOptionId",
                table: "read_aloud_recordings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Emotion",
                table: "listening_interaction_options",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Speaker",
                table: "listening_interaction_options",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_read_aloud_recordings_session_practice",
                table: "read_aloud_recordings",
                columns: new[] { "ReadingSessionId", "ListeningInteractionId", "ListeningInteractionOptionId" },
                unique: true,
                filter: "\"ListeningInteractionId\" IS NOT NULL AND \"IsDeleted\" = false")
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_read_aloud_recordings_session_practice",
                table: "read_aloud_recordings");

            migrationBuilder.DropColumn(
                name: "ListeningInteractionOptionId",
                table: "read_aloud_recordings");

            migrationBuilder.DropColumn(
                name: "Emotion",
                table: "listening_interaction_options");

            migrationBuilder.DropColumn(
                name: "Speaker",
                table: "listening_interaction_options");

            migrationBuilder.CreateIndex(
                name: "UX_read_aloud_recordings_session_practice",
                table: "read_aloud_recordings",
                columns: new[] { "ReadingSessionId", "ListeningInteractionId" },
                unique: true,
                filter: "\"ListeningInteractionId\" IS NOT NULL AND \"IsDeleted\" = false");
        }
    }
}
