using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEventResultsPublication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_event_awards_ActiveActivitySlot",
                table: "event_awards");

            migrationBuilder.DropIndex(
                name: "IX_event_awards_ActiveSlot",
                table: "event_awards");

            migrationBuilder.AddColumn<DateTime>(
                name: "EndedNotifiedAt",
                table: "events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResultsPublishedAt",
                table: "events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RevokedAt",
                table: "event_awards",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RevokedByUserId",
                table: "event_awards",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RevokedReason",
                table: "event_awards",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShowChildNames",
                table: "event_awards",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "event_appeals",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "Verification");

            migrationBuilder.AddColumn<int>(
                name: "TargetAwardId",
                table: "event_appeals",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_event_awards_ActiveActivitySlot",
                table: "event_awards",
                columns: new[] { "EventId", "EventActivityId", "RankOrTier", "SlotIndex" },
                unique: true,
                filter: "\"EventActivityId\" IS NOT NULL AND \"Status\" NOT IN ('Expired', 'UnclaimedBudgetReturned', 'Failed', 'Revoked') AND NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_event_awards_ActiveSlot",
                table: "event_awards",
                columns: new[] { "EventId", "TrackId", "RankOrTier", "SlotIndex" },
                unique: true,
                filter: "\"TrackId\" IS NOT NULL AND \"Status\" NOT IN ('Expired', 'UnclaimedBudgetReturned', 'Failed', 'Revoked') AND NOT \"IsDeleted\"");

            // Sự kiện đã kết thúc trước khi có tính năng: đánh dấu đã thông báo để worker không gửi hàng loạt thông báo cũ.
            migrationBuilder.Sql("""
                UPDATE events e SET "EndedNotifiedAt" = now()
                WHERE e."EndedNotifiedAt" IS NULL
                  AND (e."ResultsFinalizedAt" IS NOT NULL
                       OR ((e."ClosesAt" IS NULL OR e."ClosesAt" < now())
                           AND EXISTS (SELECT 1 FROM event_activities a WHERE a."EventId" = e."Id" AND NOT a."IsDeleted")
                           AND NOT EXISTS (SELECT 1 FROM event_activities a
                                           WHERE a."EventId" = e."Id" AND NOT a."IsDeleted"
                                             AND (a."EndAt" >= now() OR (a."ReviewCutoffAt" IS NOT NULL AND a."ReviewCutoffAt" >= now())))));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_event_awards_ActiveActivitySlot",
                table: "event_awards");

            migrationBuilder.DropIndex(
                name: "IX_event_awards_ActiveSlot",
                table: "event_awards");

            migrationBuilder.DropColumn(
                name: "EndedNotifiedAt",
                table: "events");

            migrationBuilder.DropColumn(
                name: "ResultsPublishedAt",
                table: "events");

            migrationBuilder.DropColumn(
                name: "RevokedAt",
                table: "event_awards");

            migrationBuilder.DropColumn(
                name: "RevokedByUserId",
                table: "event_awards");

            migrationBuilder.DropColumn(
                name: "RevokedReason",
                table: "event_awards");

            migrationBuilder.DropColumn(
                name: "ShowChildNames",
                table: "event_awards");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "event_appeals");

            migrationBuilder.DropColumn(
                name: "TargetAwardId",
                table: "event_appeals");

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
        }
    }
}
