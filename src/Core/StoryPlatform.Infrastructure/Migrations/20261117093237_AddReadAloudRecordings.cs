using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReadAloudRecordings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "read_aloud_recordings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReadingSessionId = table.Column<int>(type: "integer", nullable: false),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: false),
                    StorySegmentId = table.Column<int>(type: "integer", nullable: true),
                    ListeningInteractionId = table.Column<int>(type: "integer", nullable: true),
                    AudioStoragePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    AudioContentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AudioBytes = table.Column<int>(type: "integer", nullable: false),
                    GoodCount = table.Column<int>(type: "integer", nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    SkippedCount = table.Column<int>(type: "integer", nullable: false),
                    TotalCount = table.Column<int>(type: "integer", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_read_aloud_recordings", x => x.Id);
                    table.CheckConstraint("CK_read_aloud_recordings_one_target", "(\"StorySegmentId\" IS NOT NULL AND \"ListeningInteractionId\" IS NULL) OR (\"StorySegmentId\" IS NULL AND \"ListeningInteractionId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_read_aloud_recordings_listening_interactions_ListeningInter~",
                        column: x => x.ListeningInteractionId,
                        principalTable: "listening_interactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_read_aloud_recordings_reading_sessions_ReadingSessionId",
                        column: x => x.ReadingSessionId,
                        principalTable: "reading_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_read_aloud_recordings_story_segments_StorySegmentId",
                        column: x => x.StorySegmentId,
                        principalTable: "story_segments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_read_aloud_recordings_child",
                table: "read_aloud_recordings",
                column: "ChildProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_read_aloud_recordings_ListeningInteractionId",
                table: "read_aloud_recordings",
                column: "ListeningInteractionId");

            migrationBuilder.CreateIndex(
                name: "IX_read_aloud_recordings_StorySegmentId",
                table: "read_aloud_recordings",
                column: "StorySegmentId");

            migrationBuilder.CreateIndex(
                name: "UX_read_aloud_recordings_session_practice",
                table: "read_aloud_recordings",
                columns: new[] { "ReadingSessionId", "ListeningInteractionId" },
                unique: true,
                filter: "\"ListeningInteractionId\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "UX_read_aloud_recordings_session_segment",
                table: "read_aloud_recordings",
                columns: new[] { "ReadingSessionId", "StorySegmentId" },
                unique: true,
                filter: "\"StorySegmentId\" IS NOT NULL AND \"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "read_aloud_recordings");
        }
    }
}
