using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedCreatorParentEventVoting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Preserve the old appeal deadline during upgrade and rollback.
            migrationBuilder.AddColumn<DateTime>(
                name: "VotingStartAt",
                table: "events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FinalLeaderboardSnapshotJson",
                table: "events",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Format",
                table: "events",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Standard");

            migrationBuilder.AddColumn<int>(
                name: "MaxParentRegistrations",
                table: "events",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinVotesPerSubmission",
                table: "events",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResultsFinalizedAt",
                table: "events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewCutoffAt",
                table: "events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScoringMode",
                table: "events",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Rubric");

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmissionCutoffAt",
                table: "events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VotingEndAt",
                table: "events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AwardAllMissionCompleters",
                table: "event_tracks",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "EventVoteId",
                table: "event_task_progress",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetTrackType",
                table: "event_task_definitions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InsufficientVotesPolicyText",
                table: "event_rule_versions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RewardEligibilityText",
                table: "event_rule_versions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TieBreakerPolicyText",
                table: "event_rule_versions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SubmitCreditGrantAmount",
                table: "event_awards",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "event_votes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventId = table.Column<int>(type: "integer", nullable: false),
                    EventSubmissionId = table.Column<int>(type: "integer", nullable: true),
                    StoryId = table.Column<int>(type: "integer", nullable: false),
                    StoryVersionId = table.Column<int>(type: "integer", nullable: false),
                    ParticipationId = table.Column<int>(type: "integer", nullable: false),
                    ParentUserId = table.Column<int>(type: "integer", nullable: false),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: true),
                    ReadingSessionId = table.Column<int>(type: "integer", nullable: false),
                    Stars = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IsDisqualified = table.Column<bool>(type: "boolean", nullable: false),
                    DisqualifiedReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DisqualifiedByUserId = table.Column<int>(type: "integer", nullable: true),
                    DisqualifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_votes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_event_votes_event_participations_ParticipationId",
                        column: x => x.ParticipationId,
                        principalTable: "event_participations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_event_votes_event_submissions_EventSubmissionId",
                        column: x => x.EventSubmissionId,
                        principalTable: "event_submissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_event_votes_events_EventId",
                        column: x => x.EventId,
                        principalTable: "events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_event_votes_EventId_EventSubmissionId_IsDisqualified",
                table: "event_votes",
                columns: new[] { "EventId", "EventSubmissionId", "IsDisqualified" });

            migrationBuilder.CreateIndex(
                name: "IX_event_votes_EventId_StoryId_ParentUserId",
                table: "event_votes",
                columns: new[] { "EventId", "StoryId", "ParentUserId" },
                unique: true,
                filter: "NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_event_votes_EventSubmissionId",
                table: "event_votes",
                column: "EventSubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_event_votes_ParticipationId",
                table: "event_votes",
                column: "ParticipationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "event_votes");

            migrationBuilder.DropColumn(
                name: "FinalLeaderboardSnapshotJson",
                table: "events");

            migrationBuilder.DropColumn(
                name: "Format",
                table: "events");

            migrationBuilder.DropColumn(
                name: "MaxParentRegistrations",
                table: "events");

            migrationBuilder.DropColumn(
                name: "MinVotesPerSubmission",
                table: "events");

            migrationBuilder.DropColumn(
                name: "ResultsFinalizedAt",
                table: "events");

            migrationBuilder.DropColumn(
                name: "ReviewCutoffAt",
                table: "events");

            migrationBuilder.DropColumn(
                name: "ScoringMode",
                table: "events");

            migrationBuilder.DropColumn(
                name: "SubmissionCutoffAt",
                table: "events");

            migrationBuilder.DropColumn(
                name: "VotingEndAt",
                table: "events");

            migrationBuilder.DropColumn(
                name: "AwardAllMissionCompleters",
                table: "event_tracks");

            migrationBuilder.DropColumn(
                name: "EventVoteId",
                table: "event_task_progress");

            migrationBuilder.DropColumn(
                name: "TargetTrackType",
                table: "event_task_definitions");

            migrationBuilder.DropColumn(
                name: "InsufficientVotesPolicyText",
                table: "event_rule_versions");

            migrationBuilder.DropColumn(
                name: "RewardEligibilityText",
                table: "event_rule_versions");

            migrationBuilder.DropColumn(
                name: "TieBreakerPolicyText",
                table: "event_rule_versions");

            migrationBuilder.DropColumn(
                name: "SubmitCreditGrantAmount",
                table: "event_awards");

            migrationBuilder.DropColumn(
                name: "VotingStartAt",
                table: "events");
        }
    }
}
