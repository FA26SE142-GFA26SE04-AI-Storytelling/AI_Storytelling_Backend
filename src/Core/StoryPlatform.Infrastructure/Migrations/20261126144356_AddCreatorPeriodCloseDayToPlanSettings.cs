using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCreatorPeriodCloseDayToPlanSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CreatorPeriodCloseDay",
                table: "plan_settings",
                type: "integer",
                nullable: false,
                defaultValue: 8);

            migrationBuilder.UpdateData(
                table: "plan_settings",
                keyColumn: "Id",
                keyValue: 1,
                column: "CreatorPeriodCloseDay",
                value: 8);

            migrationBuilder.AddCheckConstraint(
                name: "CK_plan_settings_creator_period_close_day",
                table: "plan_settings",
                sql: "\"CreatorPeriodCloseDay\" BETWEEN 1 AND 28");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_plan_settings_creator_period_close_day",
                table: "plan_settings");

            migrationBuilder.DropColumn(
                name: "CreatorPeriodCloseDay",
                table: "plan_settings");
        }
    }
}
