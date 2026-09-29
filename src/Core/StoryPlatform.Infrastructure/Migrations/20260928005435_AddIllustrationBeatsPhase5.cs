using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIllustrationBeatsPhase5 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_media_assets_StorySceneId_Type",
                table: "media_assets");

            migrationBuilder.AddColumn<int>(
                name: "IllustrationBeatId",
                table: "media_assets",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "illustration_beats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StorySceneId = table.Column<int>(type: "integer", nullable: false),
                    BeatOrder = table.Column<int>(type: "integer", nullable: false),
                    StartOffset = table.Column<int>(type: "integer", nullable: false),
                    EndOffset = table.Column<int>(type: "integer", nullable: false),
                    VisualFocus = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_illustration_beats", x => x.Id);
                    table.CheckConstraint("CK_illustration_beats_offsets", "\"StartOffset\" >= 0 AND \"EndOffset\" > \"StartOffset\"");
                    table.CheckConstraint("CK_illustration_beats_order", "\"BeatOrder\" BETWEEN 1 AND 3");
                    table.ForeignKey(
                        name: "FK_illustration_beats_story_scenes_StorySceneId",
                        column: x => x.StorySceneId,
                        principalTable: "story_scenes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Pin one whole-scene beat for every pre-existing scene, including scenes whose
            // media job has not produced an illustration yet. TextRange uses .NET UTF-16
            // offsets; SQL char_length() would be wrong for non-BMP characters.
            migrationBuilder.Sql("""
                INSERT INTO illustration_beats
                    ("StorySceneId", "BeatOrder", "StartOffset", "EndOffset",
                     "VisualFocus", "CreatedAt", "UpdatedAt", "IsDeleted")
                SELECT s."Id", 1, 0, s."TextRangeEnd" - s."TextRangeStart",
                       LEFT(COALESCE(NULLIF(BTRIM(s."VisualDescription"), ''),
                           'Minh họa khoảnh khắc chính của cảnh.'), 500),
                       s."CreatedAt", NULL, s."IsDeleted"
                FROM story_scenes AS s;

                UPDATE media_assets AS a
                SET "IllustrationBeatId" = b."Id"
                FROM illustration_beats AS b
                WHERE a."Type" = 'Illustration'
                  AND a."StorySceneId" = b."StorySceneId";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_IllustrationBeatId_Type",
                table: "media_assets",
                columns: new[] { "IllustrationBeatId", "Type" },
                unique: true,
                filter: "\"IllustrationBeatId\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_StorySceneId",
                table: "media_assets",
                column: "StorySceneId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_media_assets_beat_must_be_null_for_audio",
                table: "media_assets",
                sql: "\"Type\" <> 'TtsAudio' OR \"IllustrationBeatId\" IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_media_assets_beat_required_for_illustration",
                table: "media_assets",
                sql: "\"Type\" <> 'Illustration' OR \"StorySceneId\" IS NULL OR \"IllustrationBeatId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_illustration_beats_StorySceneId_BeatOrder",
                table: "illustration_beats",
                columns: new[] { "StorySceneId", "BeatOrder" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_media_assets_illustration_beats_IllustrationBeatId",
                table: "media_assets",
                column: "IllustrationBeatId",
                principalTable: "illustration_beats",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rolling back to the old one-image-per-scene index cannot preserve multiple
            // active images. Abort rather than deleting or silently collapsing user media.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM media_assets
                        WHERE "Type" = 'Illustration' AND "StorySceneId" IS NOT NULL
                          AND "IsDeleted" = false
                        GROUP BY "StorySceneId"
                        HAVING COUNT(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'Cannot roll back AddIllustrationBeatsPhase5: multiple active illustrations exist for a scene';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_media_assets_illustration_beats_IllustrationBeatId",
                table: "media_assets");

            migrationBuilder.DropTable(
                name: "illustration_beats");

            migrationBuilder.DropIndex(
                name: "IX_media_assets_IllustrationBeatId_Type",
                table: "media_assets");

            migrationBuilder.DropIndex(
                name: "IX_media_assets_StorySceneId",
                table: "media_assets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_media_assets_beat_must_be_null_for_audio",
                table: "media_assets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_media_assets_beat_required_for_illustration",
                table: "media_assets");

            migrationBuilder.DropColumn(
                name: "IllustrationBeatId",
                table: "media_assets");

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_StorySceneId_Type",
                table: "media_assets",
                columns: new[] { "StorySceneId", "Type" },
                unique: true,
                filter: "\"StorySceneId\" IS NOT NULL AND \"StorySegmentId\" IS NULL AND \"IsDeleted\" = false");
        }
    }
}
