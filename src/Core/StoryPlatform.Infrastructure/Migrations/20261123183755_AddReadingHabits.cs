using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReadingHabits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HabitEnabled",
                table: "reading_schedules",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "ReadingReminderWeekday",
                table: "reading_schedules",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "ReadingReminderWeekend",
                table: "reading_schedules",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WeeklyGoalDays",
                table: "reading_schedules",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HabitEnabled",
                table: "reading_schedules");

            migrationBuilder.DropColumn(
                name: "ReadingReminderWeekday",
                table: "reading_schedules");

            migrationBuilder.DropColumn(
                name: "ReadingReminderWeekend",
                table: "reading_schedules");

            migrationBuilder.DropColumn(
                name: "WeeklyGoalDays",
                table: "reading_schedules");
        }
    }
}
