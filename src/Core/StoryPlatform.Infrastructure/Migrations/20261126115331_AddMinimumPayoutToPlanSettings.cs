using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMinimumPayoutToPlanSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "MinimumPayoutVnd",
                table: "plan_settings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.UpdateData(
                table: "plan_settings",
                keyColumn: "Id",
                keyValue: 1,
                column: "MinimumPayoutVnd",
                value: 200000L);

            migrationBuilder.AddCheckConstraint(
                name: "CK_plan_settings_min_payout",
                table: "plan_settings",
                sql: "\"MinimumPayoutVnd\" BETWEEN 0 AND 1000000000");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_plan_settings_min_payout",
                table: "plan_settings");

            migrationBuilder.DropColumn(
                name: "MinimumPayoutVnd",
                table: "plan_settings");
        }
    }
}
