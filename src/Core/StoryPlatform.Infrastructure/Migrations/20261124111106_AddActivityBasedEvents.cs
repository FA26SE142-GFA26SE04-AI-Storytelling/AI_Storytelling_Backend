using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityBasedEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_event_submissions_EventId_CreatorUserId",
                table: "event_submissions");

            migrationBuilder.DropIndex(
                name: "IX_event_participations_EventId_TrackId_UserId",
                table: "event_participations");

            migrationBuilder.DropIndex(
                name: "IX_event_awards_ActiveSlot",
                table: "event_awards");

            migrationBuilder.AddColumn<string>(
                name: "ConfigVersion",
                table: "events",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "LegacyRoleBased");

            migrationBuilder.Sql("""ALTER TABLE events ALTER COLUMN "ConfigVersion" DROP DEFAULT;""");

            migrationBuilder.AddColumn<string>(
                name: "FinalAwardRankingSnapshotJson",
                table: "events",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EventActivityId",
                table: "event_submissions",
                type: "integer",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "TrackId",
                table: "event_participations",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "EventActivityId",
                table: "event_participations",
                type: "integer",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "TrackId",
                table: "event_awards",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "EventActivityId",
                table: "event_awards",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResultSource",
                table: "event_awards",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetAudience",
                table: "event_awards",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "event_activities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventId = table.Column<int>(type: "integer", nullable: false),
                    ActivityType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AllowedAudience = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RegistrationStartAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RegistrationEndAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReviewCutoffAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    GrantAiCredits = table.Column<int>(type: "integer", nullable: false),
                    MaxSubmissionsPerUser = table.Column<int>(type: "integer", nullable: true),
                    MaxRegistrations = table.Column<int>(type: "integer", nullable: true),
                    WaiveWeeklyLibraryLimit = table.Column<bool>(type: "boolean", nullable: false),
                    StorySource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MinVotesPerSubmission = table.Column<int>(type: "integer", nullable: false),
                    ClaimWindowHours = table.Column<int>(type: "integer", nullable: false),
                    MaxReallocationRounds = table.Column<int>(type: "integer", nullable: false),
                    AwardAllMissionCompleters = table.Column<bool>(type: "boolean", nullable: false),
                    ConfigJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_activities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_event_activities_events_EventId",
                        column: x => x.EventId,
                        principalTable: "events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_event_submissions_EventActivityId",
                table: "event_submissions",
                column: "EventActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_event_submissions_EventId_CreatorUserId",
                table: "event_submissions",
                columns: new[] { "EventId", "CreatorUserId" },
                unique: true,
                filter: "\"EventActivityId\" IS NULL AND NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_event_participations_EventActivityId",
                table: "event_participations",
                column: "EventActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_event_participations_EventId_EventActivityId_UserId",
                table: "event_participations",
                columns: new[] { "EventId", "EventActivityId", "UserId" },
                unique: true,
                filter: "\"EventActivityId\" IS NOT NULL AND NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_event_participations_EventId_TrackId_UserId",
                table: "event_participations",
                columns: new[] { "EventId", "TrackId", "UserId" },
                unique: true,
                filter: "\"TrackId\" IS NOT NULL AND NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_event_awards_ActiveActivitySlot",
                table: "event_awards",
                columns: new[] { "EventId", "EventActivityId", "RankOrTier", "SlotIndex" },
                unique: true,
                filter: "\"EventActivityId\" IS NOT NULL AND \"Status\" NOT IN ('Expired', 'UnclaimedBudgetReturned', 'Failed') AND NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_event_awards_ActiveSlot",
                table: "event_awards",
                columns: new[] { "EventId", "TrackId", "RankOrTier", "SlotIndex" },
                unique: true,
                filter: "\"TrackId\" IS NOT NULL AND \"Status\" NOT IN ('Expired', 'UnclaimedBudgetReturned', 'Failed') AND NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_event_awards_EventActivityId",
                table: "event_awards",
                column: "EventActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_event_activities_EventId_ActivityType",
                table: "event_activities",
                columns: new[] { "EventId", "ActivityType" },
                unique: true,
                filter: "NOT \"IsDeleted\"");

            migrationBuilder.AddForeignKey(
                name: "FK_event_awards_event_activities_EventActivityId",
                table: "event_awards",
                column: "EventActivityId",
                principalTable: "event_activities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_event_participations_event_activities_EventActivityId",
                table: "event_participations",
                column: "EventActivityId",
                principalTable: "event_activities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_event_submissions_event_activities_EventActivityId",
                table: "event_submissions",
                column: "EventActivityId",
                principalTable: "event_activities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_event_awards_event_activities_EventActivityId",
                table: "event_awards");

            migrationBuilder.DropForeignKey(
                name: "FK_event_participations_event_activities_EventActivityId",
                table: "event_participations");

            migrationBuilder.DropForeignKey(
                name: "FK_event_submissions_event_activities_EventActivityId",
                table: "event_submissions");

            migrationBuilder.DropTable(
                name: "event_activities");

            migrationBuilder.DropIndex(
                name: "IX_event_submissions_EventActivityId",
                table: "event_submissions");

            migrationBuilder.DropIndex(
                name: "IX_event_submissions_EventId_CreatorUserId",
                table: "event_submissions");

            migrationBuilder.DropIndex(
                name: "IX_event_participations_EventActivityId",
                table: "event_participations");

            migrationBuilder.DropIndex(
                name: "IX_event_participations_EventId_EventActivityId_UserId",
                table: "event_participations");

            migrationBuilder.DropIndex(
                name: "IX_event_participations_EventId_TrackId_UserId",
                table: "event_participations");

            migrationBuilder.DropIndex(
                name: "IX_event_awards_ActiveActivitySlot",
                table: "event_awards");

            migrationBuilder.DropIndex(
                name: "IX_event_awards_ActiveSlot",
                table: "event_awards");

            migrationBuilder.DropIndex(
                name: "IX_event_awards_EventActivityId",
                table: "event_awards");

            migrationBuilder.DropColumn(
                name: "ConfigVersion",
                table: "events");

            migrationBuilder.DropColumn(
                name: "FinalAwardRankingSnapshotJson",
                table: "events");

            migrationBuilder.DropColumn(
                name: "EventActivityId",
                table: "event_submissions");

            migrationBuilder.DropColumn(
                name: "EventActivityId",
                table: "event_participations");

            migrationBuilder.DropColumn(
                name: "EventActivityId",
                table: "event_awards");

            migrationBuilder.DropColumn(
                name: "ResultSource",
                table: "event_awards");

            migrationBuilder.DropColumn(
                name: "TargetAudience",
                table: "event_awards");

            migrationBuilder.AlterColumn<int>(
                name: "TrackId",
                table: "event_participations",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "TrackId",
                table: "event_awards",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_event_submissions_EventId_CreatorUserId",
                table: "event_submissions",
                columns: new[] { "EventId", "CreatorUserId" },
                unique: true,
                filter: "NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_event_participations_EventId_TrackId_UserId",
                table: "event_participations",
                columns: new[] { "EventId", "TrackId", "UserId" },
                unique: true,
                filter: "NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_event_awards_ActiveSlot",
                table: "event_awards",
                columns: new[] { "EventId", "TrackId", "RankOrTier", "SlotIndex" },
                unique: true,
                filter: "\"Status\" NOT IN ('Expired', 'UnclaimedBudgetReturned', 'Failed') AND NOT \"IsDeleted\"");
        }
    }
}
