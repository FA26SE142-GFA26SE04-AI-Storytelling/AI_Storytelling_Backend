using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameContentCategoryCreatedByModerator : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_content_categories_user_accounts_CreatedByAdminId",
                table: "content_categories");

            migrationBuilder.RenameColumn(
                name: "CreatedByAdminId",
                table: "content_categories",
                newName: "CreatedByModeratorId");

            migrationBuilder.RenameIndex(
                name: "IX_content_categories_CreatedByAdminId",
                table: "content_categories",
                newName: "IX_content_categories_CreatedByModeratorId");

            migrationBuilder.AddForeignKey(
                name: "FK_content_categories_user_accounts_CreatedByModeratorId",
                table: "content_categories",
                column: "CreatedByModeratorId",
                principalTable: "user_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_content_categories_user_accounts_CreatedByModeratorId",
                table: "content_categories");

            migrationBuilder.RenameColumn(
                name: "CreatedByModeratorId",
                table: "content_categories",
                newName: "CreatedByAdminId");

            migrationBuilder.RenameIndex(
                name: "IX_content_categories_CreatedByModeratorId",
                table: "content_categories",
                newName: "IX_content_categories_CreatedByAdminId");

            migrationBuilder.AddForeignKey(
                name: "FK_content_categories_user_accounts_CreatedByAdminId",
                table: "content_categories",
                column: "CreatedByAdminId",
                principalTable: "user_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
