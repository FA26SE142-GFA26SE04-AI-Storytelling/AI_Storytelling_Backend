using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStorySummaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "story_summaries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReadingSessionId = table.Column<int>(type: "integer", nullable: false),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: false),
                    StoryId = table.Column<int>(type: "integer", nullable: false),
                    StoryVersionId = table.Column<int>(type: "integer", nullable: true),
                    AudioStoragePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AudioContentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    AudioBytes = table.Column<int>(type: "integer", nullable: true),
                    RecordingSentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FlagsSentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_story_summaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_story_summaries_child_profiles_ChildProfileId",
                        column: x => x.ChildProfileId,
                        principalTable: "child_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_story_summaries_reading_sessions_ReadingSessionId",
                        column: x => x.ReadingSessionId,
                        principalTable: "reading_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_story_summaries_stories_StoryId",
                        column: x => x.StoryId,
                        principalTable: "stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "story_summary_flags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StorySummaryId = table.Column<int>(type: "integer", nullable: false),
                    StorySceneId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_story_summary_flags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_story_summary_flags_story_scenes_StorySceneId",
                        column: x => x.StorySceneId,
                        principalTable: "story_scenes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_story_summary_flags_story_summaries_StorySummaryId",
                        column: x => x.StorySummaryId,
                        principalTable: "story_summaries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_story_summaries_child_created",
                table: "story_summaries",
                columns: new[] { "ChildProfileId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_story_summaries_StoryId",
                table: "story_summaries",
                column: "StoryId");

            migrationBuilder.CreateIndex(
                name: "UX_story_summaries_one_per_session",
                table: "story_summaries",
                column: "ReadingSessionId",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_story_summary_flags_StorySceneId",
                table: "story_summary_flags",
                column: "StorySceneId");

            migrationBuilder.CreateIndex(
                name: "UX_story_summary_flags_summary_scene",
                table: "story_summary_flags",
                columns: new[] { "StorySummaryId", "StorySceneId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "story_summary_flags");

            migrationBuilder.DropTable(
                name: "story_summaries");
        }
    }
}
