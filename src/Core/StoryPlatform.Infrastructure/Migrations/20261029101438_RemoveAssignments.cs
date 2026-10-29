using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM notifications WHERE \"Type\" IN ('AssignmentCancelled', 'StoryShared');");

            migrationBuilder.DropForeignKey(
                name: "FK_reading_sessions_assignment_recipients_AssignmentRecipientId",
                table: "reading_sessions");

            migrationBuilder.DropTable(
                name: "assignment_recipients");

            migrationBuilder.DropTable(
                name: "assignments");

            migrationBuilder.DropIndex(
                name: "IX_reading_sessions_AssignmentRecipientId",
                table: "reading_sessions");

            migrationBuilder.DropColumn(
                name: "AssignmentRecipientId",
                table: "reading_sessions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AssignmentRecipientId",
                table: "reading_sessions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "assignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AssignedByUserId = table.Column<int>(type: "integer", nullable: false),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: true),
                    StoryId = table.Column<int>(type: "integer", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DueAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_assignments_child_profiles_ChildProfileId",
                        column: x => x.ChildProfileId,
                        principalTable: "child_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_assignments_stories_StoryId",
                        column: x => x.StoryId,
                        principalTable: "stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_assignments_user_accounts_AssignedByUserId",
                        column: x => x.AssignedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "assignment_recipients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AssignmentId = table.Column<int>(type: "integer", nullable: false),
                    CancelledByUserId = table.Column<int>(type: "integer", nullable: true),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: false),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_assignment_recipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_assignment_recipients_assignments_AssignmentId",
                        column: x => x.AssignmentId,
                        principalTable: "assignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_assignment_recipients_child_profiles_ChildProfileId",
                        column: x => x.ChildProfileId,
                        principalTable: "child_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_assignment_recipients_user_accounts_CancelledByUserId",
                        column: x => x.CancelledByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_reading_sessions_AssignmentRecipientId",
                table: "reading_sessions",
                column: "AssignmentRecipientId");

            migrationBuilder.CreateIndex(
                name: "IX_assignment_recipients_AssignmentId_ChildProfileId",
                table: "assignment_recipients",
                columns: new[] { "AssignmentId", "ChildProfileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_assignment_recipients_CancelledByUserId",
                table: "assignment_recipients",
                column: "CancelledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_assignment_recipients_ChildProfileId",
                table: "assignment_recipients",
                column: "ChildProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_assignments_AssignedByUserId",
                table: "assignments",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_assignments_ChildProfileId",
                table: "assignments",
                column: "ChildProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_assignments_StoryId",
                table: "assignments",
                column: "StoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_reading_sessions_assignment_recipients_AssignmentRecipientId",
                table: "reading_sessions",
                column: "AssignmentRecipientId",
                principalTable: "assignment_recipients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
