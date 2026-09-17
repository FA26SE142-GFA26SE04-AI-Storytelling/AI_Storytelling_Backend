using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPhase5MediaGenerationWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_media_assets_StoryVersionId",
                table: "media_assets");

            migrationBuilder.AddColumn<int>(
                name: "StorySceneId",
                table: "media_assets",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "media_contexts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoryVersionId = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    ContextJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_contexts", x => x.Id);
                    table.CheckConstraint("CK_media_contexts_revision", "\"Revision\" > 0");
                    table.ForeignKey(
                        name: "FK_media_contexts_story_versions_StoryVersionId",
                        column: x => x.StoryVersionId,
                        principalTable: "story_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "story_scenes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoryVersionId = table.Column<int>(type: "integer", nullable: false),
                    SceneIndex = table.Column<int>(type: "integer", nullable: false),
                    TextRangeStart = table.Column<int>(type: "integer", nullable: false),
                    TextRangeEnd = table.Column<int>(type: "integer", nullable: false),
                    SceneText = table.Column<string>(type: "text", nullable: false),
                    VisualDescription = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_story_scenes", x => x.Id);
                    table.CheckConstraint("CK_story_scenes_scene_index", "\"SceneIndex\" >= 0");
                    table.CheckConstraint("CK_story_scenes_text_range", "\"TextRangeStart\" >= 0 AND \"TextRangeEnd\" > \"TextRangeStart\"");
                    table.ForeignKey(
                        name: "FK_story_scenes_story_versions_StoryVersionId",
                        column: x => x.StoryVersionId,
                        principalTable: "story_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

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

            migrationBuilder.CreateIndex(
                name: "IX_media_contexts_StoryVersionId_Revision",
                table: "media_contexts",
                columns: new[] { "StoryVersionId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_story_scenes_StoryVersionId_SceneIndex",
                table: "story_scenes",
                columns: new[] { "StoryVersionId", "SceneIndex" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_media_assets_story_scenes_StorySceneId",
                table: "media_assets",
                column: "StorySceneId",
                principalTable: "story_scenes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_media_assets_story_scenes_StorySceneId",
                table: "media_assets");

            migrationBuilder.DropTable(
                name: "media_contexts");

            migrationBuilder.DropTable(
                name: "story_scenes");

            migrationBuilder.DropIndex(
                name: "IX_media_assets_StorySceneId",
                table: "media_assets");

            migrationBuilder.DropIndex(
                name: "IX_media_assets_StoryVersionId_StorySceneId_Type",
                table: "media_assets");

            migrationBuilder.DropColumn(
                name: "StorySceneId",
                table: "media_assets");

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_StoryVersionId",
                table: "media_assets",
                column: "StoryVersionId");
        }
    }
}
