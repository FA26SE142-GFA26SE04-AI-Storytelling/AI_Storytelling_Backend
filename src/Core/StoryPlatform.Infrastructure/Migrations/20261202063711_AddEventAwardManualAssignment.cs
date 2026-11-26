using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEventAwardManualAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ManualAssignmentNote",
                table: "event_awards",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ManuallyAssignedAt",
                table: "event_awards",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ManuallyAssignedByUserId",
                table: "event_awards",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ManualAssignmentNote",
                table: "event_awards");

            migrationBuilder.DropColumn(
                name: "ManuallyAssignedAt",
                table: "event_awards");

            migrationBuilder.DropColumn(
                name: "ManuallyAssignedByUserId",
                table: "event_awards");
        }
    }
}
