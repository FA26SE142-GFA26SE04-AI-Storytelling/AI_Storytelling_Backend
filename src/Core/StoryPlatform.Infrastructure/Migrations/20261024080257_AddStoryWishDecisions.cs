using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStoryWishDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeclineReason",
                table: "story_wishes",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiryWarnedAt",
                table: "story_wishes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GeneratedStoryId",
                table: "story_wishes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GenerationRequestId",
                table: "story_wishes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolutionNote",
                table: "story_wishes",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResolvedByUserId",
                table: "story_wishes",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_story_wishes_GeneratedStoryId",
                table: "story_wishes",
                column: "GeneratedStoryId");

            migrationBuilder.CreateIndex(
                name: "IX_story_wishes_status_pending_since",
                table: "story_wishes",
                columns: new[] { "Status", "PendingSince" });

            migrationBuilder.AddForeignKey(
                name: "FK_story_wishes_stories_GeneratedStoryId",
                table: "story_wishes",
                column: "GeneratedStoryId",
                principalTable: "stories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_story_wishes_stories_GeneratedStoryId",
                table: "story_wishes");

            migrationBuilder.DropIndex(
                name: "IX_story_wishes_GeneratedStoryId",
                table: "story_wishes");

            migrationBuilder.DropIndex(
                name: "IX_story_wishes_status_pending_since",
                table: "story_wishes");

            migrationBuilder.DropColumn(
                name: "DeclineReason",
                table: "story_wishes");

            migrationBuilder.DropColumn(
                name: "ExpiryWarnedAt",
                table: "story_wishes");

            migrationBuilder.DropColumn(
                name: "GeneratedStoryId",
                table: "story_wishes");

            migrationBuilder.DropColumn(
                name: "GenerationRequestId",
                table: "story_wishes");

            migrationBuilder.DropColumn(
                name: "ResolutionNote",
                table: "story_wishes");

            migrationBuilder.DropColumn(
                name: "ResolvedByUserId",
                table: "story_wishes");
        }
    }
}
