using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPhase1AIStoryInput : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "stories",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AddColumn<int>(
                name: "ReadingLevel",
                table: "stories",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VocabularyLevel",
                table: "stories",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "story_generation_requests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoryId = table.Column<int>(type: "integer", nullable: false),
                    SubmittedByUserId = table.Column<int>(type: "integer", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    InputFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContextFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContextSnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    AcceptedInputJson = table.Column<string>(type: "jsonb", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LastRetryKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AttemptStartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    GuardrailDecision = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ReasonCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FallbackMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CanRetry = table.Column<bool>(type: "boolean", nullable: false),
                    GuardrailCheckVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    GuardrailCheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    HandoffJobId = table.Column<int>(type: "integer", nullable: true),
                    HandoffCreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_story_generation_requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_story_generation_requests_stories_StoryId",
                        column: x => x.StoryId,
                        principalTable: "stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_story_generation_requests_story_generation_jobs_HandoffJobId",
                        column: x => x.HandoffJobId,
                        principalTable: "story_generation_jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_story_generation_requests_user_accounts_SubmittedByUserId",
                        column: x => x.SubmittedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_story_generation_requests_HandoffJobId",
                table: "story_generation_requests",
                column: "HandoffJobId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_story_generation_requests_StoryId",
                table: "story_generation_requests",
                column: "StoryId",
                unique: true,
                filter: "\"Status\" IN ('PendingInput', 'CheckingInput', 'InputAccepted') AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_story_generation_requests_SubmittedByUserId_IdempotencyKey",
                table: "story_generation_requests",
                columns: new[] { "SubmittedByUserId", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "story_generation_requests");

            migrationBuilder.DropColumn(
                name: "ReadingLevel",
                table: "stories");

            migrationBuilder.DropColumn(
                name: "VocabularyLevel",
                table: "stories");

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "stories",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);
        }
    }
}
