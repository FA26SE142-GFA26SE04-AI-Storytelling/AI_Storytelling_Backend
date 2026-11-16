using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExcludeReadAloudFromListeningAnchorIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_listening_interactions_ParentStoryPackageId_AnchorOffset",
                table: "listening_interactions");

            migrationBuilder.DropIndex(
                name: "IX_listening_interactions_StoryVersionId_AnchorOffset",
                table: "listening_interactions");

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_listening_interactions_ParentStoryPackageId_AnchorOffset",
                table: "listening_interactions");

            migrationBuilder.DropIndex(
                name: "IX_listening_interactions_StoryVersionId_AnchorOffset",
                table: "listening_interactions");

            migrationBuilder.CreateIndex(
                name: "IX_listening_interactions_ParentStoryPackageId_AnchorOffset",
                table: "listening_interactions",
                columns: new[] { "ParentStoryPackageId", "AnchorOffset" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"ParentStoryPackageId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_listening_interactions_StoryVersionId_AnchorOffset",
                table: "listening_interactions",
                columns: new[] { "StoryVersionId", "AnchorOffset" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"ParentStoryPackageId\" IS NULL");
        }
    }
}
