using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStoryCreditHoldToCreditUsages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ConsumedAt",
                table: "credit_usages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StoryId",
                table: "credit_usages",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_credit_usages_StoryId",
                table: "credit_usages",
                column: "StoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_credit_usages_stories_StoryId",
                table: "credit_usages",
                column: "StoryId",
                principalTable: "stories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_credit_usages_stories_StoryId",
                table: "credit_usages");

            migrationBuilder.DropIndex(
                name: "IX_credit_usages_StoryId",
                table: "credit_usages");

            migrationBuilder.DropColumn(
                name: "ConsumedAt",
                table: "credit_usages");

            migrationBuilder.DropColumn(
                name: "StoryId",
                table: "credit_usages");
        }
    }
}
