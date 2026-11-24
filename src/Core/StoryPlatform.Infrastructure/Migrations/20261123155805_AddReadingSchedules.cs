using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReadingSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reading_schedules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    BedtimeWeekday = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    BedtimeWeekend = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    BreakEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    BreakAfterMinutes = table.Column<int>(type: "integer", nullable: false),
                    BreakDurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    BreakRemindEyes = table.Column<bool>(type: "boolean", nullable: false),
                    BreakRemindWater = table.Column<bool>(type: "boolean", nullable: false),
                    BreakRemindMove = table.Column<bool>(type: "boolean", nullable: false),
                    ScreenLimitEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ScreenLimitWeekdayMinutes = table.Column<int>(type: "integer", nullable: false),
                    ScreenLimitWeekendMinutes = table.Column<int>(type: "integer", nullable: false),
                    ContinuousReadingSince = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastReadingSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BreakUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UsageDate = table.Column<DateOnly>(type: "date", nullable: true),
                    UsageSeconds = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reading_schedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_reading_schedules_child_profiles_ChildProfileId",
                        column: x => x.ChildProfileId,
                        principalTable: "child_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reading_schedule_windows",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReadingScheduleId = table.Column<int>(type: "integer", nullable: false),
                    DayType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Slot = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reading_schedule_windows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_reading_schedule_windows_reading_schedules_ReadingScheduleId",
                        column: x => x.ReadingScheduleId,
                        principalTable: "reading_schedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_reading_schedule_windows_ReadingScheduleId_DayType_Slot",
                table: "reading_schedule_windows",
                columns: new[] { "ReadingScheduleId", "DayType", "Slot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reading_schedules_ChildProfileId",
                table: "reading_schedules",
                column: "ChildProfileId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reading_schedule_windows");

            migrationBuilder.DropTable(
                name: "reading_schedules");
        }
    }
}
