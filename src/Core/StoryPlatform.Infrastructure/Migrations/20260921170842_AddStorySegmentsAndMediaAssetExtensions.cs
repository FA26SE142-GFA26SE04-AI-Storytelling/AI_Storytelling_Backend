using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStorySegmentsAndMediaAssetExtensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_media_assets_StorySceneId",
                table: "media_assets");

            migrationBuilder.DropIndex(
                name: "IX_media_assets_StoryVersionId_StorySceneId_Type",
                table: "media_assets");

            migrationBuilder.AddColumn<int>(
                name: "AttemptCount",
                table: "media_assets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAt",
                table: "media_assets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastValidationReason",
                table: "media_assets",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MimeType",
                table: "media_assets",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Model",
                table: "media_assets",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "media_assets",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderAssetId",
                table: "media_assets",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderRequestId",
                table: "media_assets",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReferenceImageAssetId",
                table: "media_assets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StorySegmentId",
                table: "media_assets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ValidationResultJson",
                table: "media_assets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ValidationStatus",
                table: "media_assets",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "story_segments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StorySceneId = table.Column<int>(type: "integer", nullable: false),
                    SegmentOrder = table.Column<int>(type: "integer", nullable: false),
                    StartOffset = table.Column<int>(type: "integer", nullable: false),
                    EndOffset = table.Column<int>(type: "integer", nullable: false),
                    TextContent = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_story_segments", x => x.Id);
                    table.CheckConstraint("CK_story_segments_offsets", "\"StartOffset\" >= 0 AND \"EndOffset\" > \"StartOffset\"");
                    table.CheckConstraint("CK_story_segments_segment_order", "\"SegmentOrder\" >= 1");
                    table.ForeignKey(
                        name: "FK_story_segments_story_scenes_StorySceneId",
                        column: x => x.StorySceneId,
                        principalTable: "story_scenes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_StorySceneId_Type",
                table: "media_assets",
                columns: new[] { "StorySceneId", "Type" },
                unique: true,
                filter: "\"StorySceneId\" IS NOT NULL AND \"StorySegmentId\" IS NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_StorySegmentId_Type",
                table: "media_assets",
                columns: new[] { "StorySegmentId", "Type" },
                unique: true,
                filter: "\"StorySegmentId\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_StoryVersionId",
                table: "media_assets",
                column: "StoryVersionId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_media_assets_attempt_count",
                table: "media_assets",
                sql: "\"AttemptCount\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_media_assets_segment_must_be_null_for_illustration",
                table: "media_assets",
                sql: "\"Type\" <> 'Illustration' OR \"StorySegmentId\" IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_media_assets_segment_required_for_audio",
                table: "media_assets",
                sql: "\"Type\" <> 'TtsAudio' OR \"StorySegmentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_story_segments_StorySceneId_SegmentOrder",
                table: "story_segments",
                columns: new[] { "StorySceneId", "SegmentOrder" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_media_assets_story_segments_StorySegmentId",
                table: "media_assets",
                column: "StorySegmentId",
                principalTable: "story_segments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_media_assets_story_segments_StorySegmentId",
                table: "media_assets");

            migrationBuilder.DropTable(
                name: "story_segments");

            migrationBuilder.DropIndex(
                name: "IX_media_assets_StorySceneId_Type",
                table: "media_assets");

            migrationBuilder.DropIndex(
                name: "IX_media_assets_StorySegmentId_Type",
                table: "media_assets");

            migrationBuilder.DropIndex(
                name: "IX_media_assets_StoryVersionId",
                table: "media_assets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_media_assets_attempt_count",
                table: "media_assets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_media_assets_segment_must_be_null_for_illustration",
                table: "media_assets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_media_assets_segment_required_for_audio",
                table: "media_assets");

            migrationBuilder.DropColumn(
                name: "AttemptCount",
                table: "media_assets");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "media_assets");

            migrationBuilder.DropColumn(
                name: "LastValidationReason",
                table: "media_assets");

            migrationBuilder.DropColumn(
                name: "MimeType",
                table: "media_assets");

            migrationBuilder.DropColumn(
                name: "Model",
                table: "media_assets");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "media_assets");

            migrationBuilder.DropColumn(
                name: "ProviderAssetId",
                table: "media_assets");

            migrationBuilder.DropColumn(
                name: "ProviderRequestId",
                table: "media_assets");

            migrationBuilder.DropColumn(
                name: "ReferenceImageAssetId",
                table: "media_assets");

            migrationBuilder.DropColumn(
                name: "StorySegmentId",
                table: "media_assets");

            migrationBuilder.DropColumn(
                name: "ValidationResultJson",
                table: "media_assets");

            migrationBuilder.DropColumn(
                name: "ValidationStatus",
                table: "media_assets");

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_StorySceneId",
                table: "media_assets",
                column: "StorySceneId");

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_StoryVersionId_StorySceneId_Type",
                table: "media_assets",
                columns: new[] { "StoryVersionId", "StorySceneId", "Type" },
                unique: true,
                filter: "\"StorySceneId\" IS NOT NULL AND \"IsDeleted\" = false");
        }
    }
}
