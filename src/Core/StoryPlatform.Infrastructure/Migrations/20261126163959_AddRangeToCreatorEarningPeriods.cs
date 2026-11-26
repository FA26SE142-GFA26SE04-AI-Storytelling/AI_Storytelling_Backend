using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRangeToCreatorEarningPeriods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_creator_earning_periods_PeriodMonth",
                table: "creator_earning_periods");

            migrationBuilder.AddColumn<DateTime>(
                name: "PeriodEnd",
                table: "creator_earning_periods",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "PeriodStart",
                table: "creator_earning_periods",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // Các kỳ đã chốt trước đây đều là trọn một tháng.
            migrationBuilder.Sql("""
                UPDATE creator_earning_periods
                SET "PeriodStart" = "PeriodMonth",
                    "PeriodEnd" = "PeriodMonth" + INTERVAL '1 month' - INTERVAL '1 day';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_creator_earning_periods_PeriodStart",
                table: "creator_earning_periods",
                column: "PeriodStart",
                unique: true,
                filter: "NOT \"IsDeleted\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_creator_earning_periods_range",
                table: "creator_earning_periods",
                sql: "\"PeriodEnd\" >= \"PeriodStart\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_creator_earning_periods_PeriodStart",
                table: "creator_earning_periods");

            migrationBuilder.DropCheckConstraint(
                name: "CK_creator_earning_periods_range",
                table: "creator_earning_periods");

            migrationBuilder.DropColumn(
                name: "PeriodEnd",
                table: "creator_earning_periods");

            migrationBuilder.DropColumn(
                name: "PeriodStart",
                table: "creator_earning_periods");

            migrationBuilder.CreateIndex(
                name: "IX_creator_earning_periods_PeriodMonth",
                table: "creator_earning_periods",
                column: "PeriodMonth",
                unique: true,
                filter: "NOT \"IsDeleted\"");
        }
    }
}
