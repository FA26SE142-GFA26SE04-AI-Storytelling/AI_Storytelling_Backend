using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonalScopeToTokenQuota : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "token_quota_configs",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_token_quota_configs_UserId",
                table: "token_quota_configs",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_token_quota_configs_user_accounts_UserId",
                table: "token_quota_configs",
                column: "UserId",
                principalTable: "user_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_token_quota_configs_user_accounts_UserId",
                table: "token_quota_configs");

            migrationBuilder.DropIndex(
                name: "IX_token_quota_configs_UserId",
                table: "token_quota_configs");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "token_quota_configs");
        }
    }
}
