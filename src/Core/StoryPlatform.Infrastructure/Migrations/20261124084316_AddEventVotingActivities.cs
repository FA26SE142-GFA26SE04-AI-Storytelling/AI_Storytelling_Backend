using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEventVotingActivities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreatorVotingScheduleJson",
                table: "events",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ReadingSessionId",
                table: "event_votes",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "ParentSubmissionText",
                table: "event_submissions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParentSubmissionTitle",
                table: "event_submissions",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM events WHERE "CreatorVotingScheduleJson" IS NOT NULL)
                        OR EXISTS (SELECT 1 FROM event_submissions WHERE "ParentSubmissionText" IS NOT NULL)
                        OR EXISTS (SELECT 1 FROM event_votes WHERE "ReadingSessionId" IS NULL) THEN
                        RAISE EXCEPTION 'Cannot roll back event voting activities with reverse-voting data';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropColumn(
                name: "CreatorVotingScheduleJson",
                table: "events");

            migrationBuilder.DropColumn(
                name: "ParentSubmissionText",
                table: "event_submissions");

            migrationBuilder.DropColumn(
                name: "ParentSubmissionTitle",
                table: "event_submissions");

            migrationBuilder.AlterColumn<int>(
                name: "ReadingSessionId",
                table: "event_votes",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
