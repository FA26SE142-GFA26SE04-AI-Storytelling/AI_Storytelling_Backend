using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropTokenQuotaChildScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM token_quota_configs WHERE \"Scope\" = 'Child';");

            migrationBuilder.DropForeignKey(
                name: "FK_token_quota_configs_child_profiles_ChildProfileId",
                table: "token_quota_configs");

            migrationBuilder.DropIndex(
                name: "IX_token_quota_configs_ChildProfileId",
                table: "token_quota_configs");

            migrationBuilder.DropColumn(
                name: "ChildProfileId",
                table: "token_quota_configs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ChildProfileId",
                table: "token_quota_configs",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_token_quota_configs_ChildProfileId",
                table: "token_quota_configs",
                column: "ChildProfileId");

            migrationBuilder.AddForeignKey(
                name: "FK_token_quota_configs_child_profiles_ChildProfileId",
                table: "token_quota_configs",
                column: "ChildProfileId",
                principalTable: "child_profiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
