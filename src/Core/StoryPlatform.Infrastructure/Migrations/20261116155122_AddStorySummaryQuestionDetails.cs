using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStorySummaryQuestionDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_story_summary_flags_summary_scene",
                table: "story_summary_flags");

            migrationBuilder.AddColumn<DateTime>(
                name: "AnsweredAt",
                table: "story_summary_flags",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AudioBytes",
                table: "story_summary_flags",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AudioContentType",
                table: "story_summary_flags",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AudioStoragePath",
                table: "story_summary_flags",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IllustrationBeatId",
                table: "story_summary_flags",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MediaAssetId",
                table: "story_summary_flags",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuestionText",
                table: "story_summary_flags",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_story_summary_flags_IllustrationBeatId",
                table: "story_summary_flags",
                column: "IllustrationBeatId");

            migrationBuilder.CreateIndex(
                name: "IX_story_summary_flags_MediaAssetId",
                table: "story_summary_flags",
                column: "MediaAssetId");

            migrationBuilder.CreateIndex(
                name: "UX_story_summary_flags_summary_beat",
                table: "story_summary_flags",
                columns: new[] { "StorySummaryId", "IllustrationBeatId" },
                unique: true,
                filter: "\"IllustrationBeatId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_story_summary_flags_summary_scene",
                table: "story_summary_flags",
                columns: new[] { "StorySummaryId", "StorySceneId" },
                unique: true,
                filter: "\"IllustrationBeatId\" IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_story_summary_flags_illustration_beats_IllustrationBeatId",
                table: "story_summary_flags",
                column: "IllustrationBeatId",
                principalTable: "illustration_beats",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_story_summary_flags_media_assets_MediaAssetId",
                table: "story_summary_flags",
                column: "MediaAssetId",
                principalTable: "media_assets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_story_summary_flags_illustration_beats_IllustrationBeatId",
                table: "story_summary_flags");

            migrationBuilder.DropForeignKey(
                name: "FK_story_summary_flags_media_assets_MediaAssetId",
                table: "story_summary_flags");

            migrationBuilder.DropIndex(
                name: "IX_story_summary_flags_IllustrationBeatId",
                table: "story_summary_flags");

            migrationBuilder.DropIndex(
                name: "IX_story_summary_flags_MediaAssetId",
                table: "story_summary_flags");

            migrationBuilder.DropIndex(
                name: "UX_story_summary_flags_summary_beat",
                table: "story_summary_flags");

            migrationBuilder.DropIndex(
                name: "UX_story_summary_flags_summary_scene",
                table: "story_summary_flags");

            migrationBuilder.DropColumn(
                name: "AnsweredAt",
                table: "story_summary_flags");

            migrationBuilder.DropColumn(
                name: "AudioBytes",
                table: "story_summary_flags");

            migrationBuilder.DropColumn(
                name: "AudioContentType",
                table: "story_summary_flags");

            migrationBuilder.DropColumn(
                name: "AudioStoragePath",
                table: "story_summary_flags");

            migrationBuilder.DropColumn(
                name: "IllustrationBeatId",
                table: "story_summary_flags");

            migrationBuilder.DropColumn(
                name: "MediaAssetId",
                table: "story_summary_flags");

            migrationBuilder.DropColumn(
                name: "QuestionText",
                table: "story_summary_flags");

            migrationBuilder.CreateIndex(
                name: "UX_story_summary_flags_summary_scene",
                table: "story_summary_flags",
                columns: new[] { "StorySummaryId", "StorySceneId" },
                unique: true);
        }
    }
}
