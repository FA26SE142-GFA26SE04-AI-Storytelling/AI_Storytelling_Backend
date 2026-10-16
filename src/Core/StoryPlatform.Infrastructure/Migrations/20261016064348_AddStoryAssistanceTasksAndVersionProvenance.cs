using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStoryAssistanceTasksAndVersionProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssistanceProvenanceJson",
                table: "story_versions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "story_assistance_steps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TaskId = table.Column<int>(type: "integer", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    OperationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    InputJson = table.Column<string>(type: "jsonb", nullable: false),
                    OutputJson = table.Column<string>(type: "jsonb", nullable: true),
                    ResponseJson = table.Column<string>(type: "text", nullable: true),
                    ResultingTaskRevision = table.Column<int>(type: "integer", nullable: false),
                    DirectionStepId = table.Column<int>(type: "integer", nullable: true),
                    Provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PromptVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_story_assistance_steps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_story_assistance_steps_story_assistance_steps_DirectionStep~",
                        column: x => x.DirectionStepId,
                        principalTable: "story_assistance_steps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "story_assistance_tasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoryId = table.Column<int>(type: "integer", nullable: false),
                    OwnerUserId = table.Column<int>(type: "integer", nullable: false),
                    CreateOperationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreateRequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreateResponseJson = table.Column<string>(type: "text", nullable: false),
                    EntryPoint = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SourceVersionId = table.Column<int>(type: "integer", nullable: true),
                    SourceRevisionToken = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AudienceRevision = table.Column<int>(type: "integer", nullable: false),
                    Language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    AgeBand = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ContextJson = table.Column<string>(type: "jsonb", nullable: false),
                    SnapshotHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TaskRevision = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    LastSequence = table.Column<int>(type: "integer", nullable: false),
                    ParentTaskId = table.Column<int>(type: "integer", nullable: true),
                    SourceReviewStepId = table.Column<int>(type: "integer", nullable: true),
                    FindingId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ActiveOperationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LeaseExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ActiveClarificationStepId = table.Column<int>(type: "integer", nullable: true),
                    AcceptedAnswerStepId = table.Column<int>(type: "integer", nullable: true),
                    ActiveOptionsStepId = table.Column<int>(type: "integer", nullable: true),
                    ActiveReviewStepId = table.Column<int>(type: "integer", nullable: true),
                    ActiveDirectionStepId = table.Column<int>(type: "integer", nullable: true),
                    ActiveTrialStepId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_story_assistance_tasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_story_assistance_tasks_stories_StoryId",
                        column: x => x.StoryId,
                        principalTable: "stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_story_assistance_tasks_story_assistance_steps_SourceReviewS~",
                        column: x => x.SourceReviewStepId,
                        principalTable: "story_assistance_steps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_story_assistance_tasks_story_assistance_tasks_ParentTaskId",
                        column: x => x.ParentTaskId,
                        principalTable: "story_assistance_tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_story_assistance_tasks_story_versions_SourceVersionId",
                        column: x => x.SourceVersionId,
                        principalTable: "story_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_story_assistance_tasks_user_accounts_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_story_assistance_steps_DirectionStepId",
                table: "story_assistance_steps",
                column: "DirectionStepId");

            migrationBuilder.CreateIndex(
                name: "IX_story_assistance_steps_TaskId_OperationId",
                table: "story_assistance_steps",
                columns: new[] { "TaskId", "OperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_story_assistance_steps_TaskId_Sequence",
                table: "story_assistance_steps",
                columns: new[] { "TaskId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_story_assistance_tasks_OwnerUserId_StoryId_CreatedAt",
                table: "story_assistance_tasks",
                columns: new[] { "OwnerUserId", "StoryId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_story_assistance_tasks_ParentTaskId",
                table: "story_assistance_tasks",
                column: "ParentTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_story_assistance_tasks_SourceReviewStepId",
                table: "story_assistance_tasks",
                column: "SourceReviewStepId");

            migrationBuilder.CreateIndex(
                name: "IX_story_assistance_tasks_SourceVersionId",
                table: "story_assistance_tasks",
                column: "SourceVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_story_assistance_tasks_StoryId_OwnerUserId_CreateOperationId",
                table: "story_assistance_tasks",
                columns: new[] { "StoryId", "OwnerUserId", "CreateOperationId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_story_assistance_steps_story_assistance_tasks_TaskId",
                table: "story_assistance_steps",
                column: "TaskId",
                principalTable: "story_assistance_tasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_story_assistance_steps_story_assistance_tasks_TaskId",
                table: "story_assistance_steps");

            migrationBuilder.DropTable(
                name: "story_assistance_tasks");

            migrationBuilder.DropTable(
                name: "story_assistance_steps");

            migrationBuilder.DropColumn(
                name: "AssistanceProvenanceJson",
                table: "story_versions");
        }
    }
}
