using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLogIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_audit_logs_ActorUserId",
                table: "audit_logs");

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_Action_OccurredAt",
                table: "audit_logs",
                columns: new[] { "Action", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_ActorUserId_OccurredAt",
                table: "audit_logs",
                columns: new[] { "ActorUserId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_EntityType_EntityId",
                table: "audit_logs",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_OccurredAt",
                table: "audit_logs",
                column: "OccurredAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_audit_logs_Action_OccurredAt",
                table: "audit_logs");

            migrationBuilder.DropIndex(
                name: "IX_audit_logs_ActorUserId_OccurredAt",
                table: "audit_logs");

            migrationBuilder.DropIndex(
                name: "IX_audit_logs_EntityType_EntityId",
                table: "audit_logs");

            migrationBuilder.DropIndex(
                name: "IX_audit_logs_OccurredAt",
                table: "audit_logs");

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_ActorUserId",
                table: "audit_logs",
                column: "ActorUserId");
        }
    }
}
