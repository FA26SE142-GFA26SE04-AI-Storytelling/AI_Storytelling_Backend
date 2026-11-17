using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStoryReflections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "story_reflections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReadingSessionId = table.Column<int>(type: "integer", nullable: false),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: false),
                    StoryId = table.Column<int>(type: "integer", nullable: false),
                    StoryVersionId = table.Column<int>(type: "integer", nullable: true),
                    Emotions = table.Column<List<string>>(type: "text[]", nullable: false),
                    FavoriteSceneId = table.Column<int>(type: "integer", nullable: true),
                    FavoriteMediaAssetId = table.Column<int>(type: "integer", nullable: true),
                    NoteText = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    NoteAudioStoragePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    NoteAudioContentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    NoteAudioBytes = table.Column<int>(type: "integer", nullable: true),
                    NoteSentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_story_reflections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_story_reflections_media_assets_FavoriteMediaAssetId",
                        column: x => x.FavoriteMediaAssetId,
                        principalTable: "media_assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_story_reflections_reading_sessions_ReadingSessionId",
                        column: x => x.ReadingSessionId,
                        principalTable: "reading_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_story_reflections_story_scenes_FavoriteSceneId",
                        column: x => x.FavoriteSceneId,
                        principalTable: "story_scenes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_story_reflections_child",
                table: "story_reflections",
                column: "ChildProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_story_reflections_FavoriteMediaAssetId",
                table: "story_reflections",
                column: "FavoriteMediaAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_story_reflections_FavoriteSceneId",
                table: "story_reflections",
                column: "FavoriteSceneId");

            migrationBuilder.CreateIndex(
                name: "UX_story_reflections_one_per_session",
                table: "story_reflections",
                column: "ReadingSessionId",
                unique: true,
                filter: "\"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "story_reflections");
        }
    }
}
