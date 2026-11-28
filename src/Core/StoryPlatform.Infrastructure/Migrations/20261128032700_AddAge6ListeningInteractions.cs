using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAge6ListeningInteractions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_story_generation_jobs_StoryVersionId_ListeningInteractionTy~",
                table: "story_generation_jobs");

            migrationBuilder.DropIndex(
                name: "IX_listening_interactions_ParentStoryPackageId_AnchorOffset",
                table: "listening_interactions");

            migrationBuilder.DropIndex(
                name: "IX_listening_interactions_StoryVersionId_AnchorOffset",
                table: "listening_interactions");

            migrationBuilder.AddColumn<string>(
                name: "WordStatusesJson",
                table: "read_aloud_recordings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BlankIndex",
                table: "listening_interactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TargetAge",
                table: "listening_interactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StepResultsJson",
                table: "listening_interaction_attempts",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_story_generation_jobs_StoryVersionId_ListeningInteractionTy~",
                table: "story_generation_jobs",
                columns: new[] { "StoryVersionId", "ListeningInteractionType", "OperationKey" },
                unique: true,
                filter: "\"ParentStoryPackageId\" IS NULL AND \"Operation\" = 'GenerateListeningInteraction' AND \"StoryVersionId\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_listening_interactions_ParentStoryPackageId_TargetAge_Ancho~",
                table: "listening_interactions",
                columns: new[] { "ParentStoryPackageId", "TargetAge", "AnchorOffset" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"ParentStoryPackageId\" IS NOT NULL AND \"Type\" <> 'ReadAloud'");

            migrationBuilder.CreateIndex(
                name: "IX_listening_interactions_StoryVersionId_ParentStoryPackageId_~",
                table: "listening_interactions",
                columns: new[] { "StoryVersionId", "ParentStoryPackageId", "TargetAge" });

            migrationBuilder.CreateIndex(
                name: "IX_listening_interactions_StoryVersionId_TargetAge_AnchorOffset",
                table: "listening_interactions",
                columns: new[] { "StoryVersionId", "TargetAge", "AnchorOffset" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"ParentStoryPackageId\" IS NULL AND \"Type\" <> 'ReadAloud'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_story_generation_jobs_StoryVersionId_ListeningInteractionTy~",
                table: "story_generation_jobs");

            migrationBuilder.DropIndex(
                name: "IX_listening_interactions_ParentStoryPackageId_TargetAge_Ancho~",
                table: "listening_interactions");

            migrationBuilder.DropIndex(
                name: "IX_listening_interactions_StoryVersionId_ParentStoryPackageId_~",
                table: "listening_interactions");

            migrationBuilder.DropIndex(
                name: "IX_listening_interactions_StoryVersionId_TargetAge_AnchorOffset",
                table: "listening_interactions");

            migrationBuilder.DropColumn(
                name: "WordStatusesJson",
                table: "read_aloud_recordings");

            migrationBuilder.DropColumn(
                name: "BlankIndex",
                table: "listening_interactions");

            migrationBuilder.DropColumn(
                name: "TargetAge",
                table: "listening_interactions");

            migrationBuilder.DropColumn(
                name: "StepResultsJson",
                table: "listening_interaction_attempts");

            migrationBuilder.CreateIndex(
                name: "IX_story_generation_jobs_StoryVersionId_ListeningInteractionTy~",
                table: "story_generation_jobs",
                columns: new[] { "StoryVersionId", "ListeningInteractionType" },
                unique: true,
                filter: "\"ParentStoryPackageId\" IS NULL AND \"Operation\" = 'GenerateListeningInteraction' AND \"StoryVersionId\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_listening_interactions_ParentStoryPackageId_AnchorOffset",
                table: "listening_interactions",
                columns: new[] { "ParentStoryPackageId", "AnchorOffset" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"ParentStoryPackageId\" IS NOT NULL AND \"Type\" <> 'ReadAloud'");

            migrationBuilder.CreateIndex(
                name: "IX_listening_interactions_StoryVersionId_AnchorOffset",
                table: "listening_interactions",
                columns: new[] { "StoryVersionId", "AnchorOffset" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"ParentStoryPackageId\" IS NULL AND \"Type\" <> 'ReadAloud'");
        }
    }
}
