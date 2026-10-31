using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVocabularyAudio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StoryVocabularyId",
                table: "media_assets",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_StoryVocabularyId_Type",
                table: "media_assets",
                columns: new[] { "StoryVocabularyId", "Type" },
                unique: true,
                filter: "\"StoryVocabularyId\" IS NOT NULL AND \"IsDeleted\" = false AND \"IsCurrent\" = true");

            migrationBuilder.AddCheckConstraint(
                name: "CK_media_assets_vocabulary_required_for_vocabulary_audio",
                table: "media_assets",
                sql: "\"Type\" <> 'VocabularyAudio' OR \"StoryVocabularyId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_media_assets_story_vocabulary_StoryVocabularyId",
                table: "media_assets",
                column: "StoryVocabularyId",
                principalTable: "story_vocabulary",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_media_assets_story_vocabulary_StoryVocabularyId",
                table: "media_assets");

            migrationBuilder.DropIndex(
                name: "IX_media_assets_StoryVocabularyId_Type",
                table: "media_assets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_media_assets_vocabulary_required_for_vocabulary_audio",
                table: "media_assets");

            migrationBuilder.DropColumn(
                name: "StoryVocabularyId",
                table: "media_assets");
        }
    }
}
