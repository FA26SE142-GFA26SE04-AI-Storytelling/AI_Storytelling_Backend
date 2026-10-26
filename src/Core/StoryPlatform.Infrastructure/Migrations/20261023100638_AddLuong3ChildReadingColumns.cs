using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLuong3ChildReadingColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_badges_ChildProfileId",
                table: "badges");

            migrationBuilder.AddColumn<string>(
                name: "Feeling",
                table: "reading_sessions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "LastStreakDate",
                table: "achievements",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_badges_ChildProfileId_BadgeCode",
                table: "badges",
                columns: new[] { "ChildProfileId", "BadgeCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_badges_ChildProfileId_BadgeCode",
                table: "badges");

            migrationBuilder.DropColumn(
                name: "Feeling",
                table: "reading_sessions");

            migrationBuilder.DropColumn(
                name: "LastStreakDate",
                table: "achievements");

            migrationBuilder.CreateIndex(
                name: "IX_badges_ChildProfileId",
                table: "badges",
                column: "ChildProfileId");
        }
    }
}
