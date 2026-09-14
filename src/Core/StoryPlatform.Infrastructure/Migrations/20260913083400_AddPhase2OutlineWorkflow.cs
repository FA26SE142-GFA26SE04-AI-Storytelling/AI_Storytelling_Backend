using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPhase2OutlineWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_story_versions_StoryId",
                table: "story_versions");

            migrationBuilder.DropIndex(
                name: "IX_story_generation_jobs_StoryId",
                table: "story_generation_jobs");

            migrationBuilder.AddColumn<DateTime>(
                name: "OutlineApprovedAt",
                table: "story_versions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OutlineApprovedByUserId",
                table: "story_versions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AttemptNo",
                table: "story_generation_jobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BaseStoryVersionId",
                table: "story_generation_jobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConcurrencyToken",
                table: "story_generation_jobs",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ErrorCode",
                table: "story_generation_jobs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GenerationMetadataJson",
                table: "story_generation_jobs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GenerationRequestId",
                table: "story_generation_jobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LeaseExpiresAt",
                table: "story_generation_jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxAttempts",
                table: "story_generation_jobs",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<string>(
                name: "Operation",
                table: "story_generation_jobs",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "GenerateOutline");

            migrationBuilder.AddColumn<string>(
                name: "OperationKey",
                table: "story_generation_jobs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequestedByUserId",
                table: "story_generation_jobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "story_generation_jobs",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.AddColumn<int>(
                name: "StoryVersionId",
                table: "story_generation_jobs",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE story_generation_jobs AS job
                SET "GenerationRequestId" = request."Id",
                    "RequestedByUserId" = request."SubmittedByUserId",
                    "OperationKey" = request."IdempotencyKey"
                FROM story_generation_requests AS request
                WHERE request."HandoffJobId" = job."Id";

                UPDATE story_generation_jobs
                SET "ConcurrencyToken" = md5(random()::text || clock_timestamp()::text || "Id"::text),
                    "MaxAttempts" = 3,
                    "Operation" = 'GenerateOutline',
                    "Status" = CASE
                        WHEN "Stage" = 'InputValidated' THEN 'Pending'
                        WHEN "Stage" IN ('OutlineGenerated', 'OutlineApproved', 'Generated', 'Completed') THEN 'Completed'
                        ELSE 'Failed'
                    END,
                    "Stage" = CASE WHEN "Stage" = 'InputValidated' THEN 'OutlinePending' ELSE "Stage" END;

                WITH ranked_active_jobs AS (
                    SELECT "Id",
                           row_number() OVER (
                               PARTITION BY "StoryId"
                               ORDER BY "CreatedAt" DESC, "Id" DESC) AS active_rank
                    FROM story_generation_jobs
                    WHERE "IsDeleted" = false
                      AND "Operation" IN ('GenerateOutline', 'RegenerateOutline')
                      AND "Status" IN ('Pending', 'Processing')
                )
                UPDATE story_generation_jobs AS job
                SET "Status" = 'Cancelled',
                    "ErrorCode" = 'SUPERSEDED_DURING_PHASE2_MIGRATION',
                    "CompletedAt" = COALESCE(job."CompletedAt", now())
                FROM ranked_active_jobs AS ranked
                WHERE job."Id" = ranked."Id" AND ranked.active_rank > 1;

                WITH ranked_keys AS (
                    SELECT "Id",
                           row_number() OVER (
                               PARTITION BY "RequestedByUserId", "Operation", "OperationKey"
                               ORDER BY "CreatedAt" DESC, "Id" DESC) AS key_rank
                    FROM story_generation_jobs
                    WHERE "RequestedByUserId" IS NOT NULL
                      AND "OperationKey" IS NOT NULL
                      AND "IsDeleted" = false
                )
                UPDATE story_generation_jobs AS job
                SET "OperationKey" = NULL
                FROM ranked_keys AS ranked
                WHERE job."Id" = ranked."Id" AND ranked.key_rank > 1;

                WITH ranked_current_versions AS (
                    SELECT "Id",
                           row_number() OVER (
                               PARTITION BY "StoryId"
                               ORDER BY "VersionNo" DESC, "CreatedAt" DESC, "Id" DESC) AS current_rank
                    FROM story_versions
                    WHERE "IsDeleted" = false AND "IsCurrent" = true
                )
                UPDATE story_versions AS version
                SET "IsCurrent" = false
                FROM ranked_current_versions AS ranked
                WHERE version."Id" = ranked."Id" AND ranked.current_rank > 1;

                WITH marked_versions AS (
                    SELECT "Id", "StoryId", "VersionNo",
                           row_number() OVER (
                               PARTITION BY "StoryId", "VersionNo"
                               ORDER BY "CreatedAt", "Id") AS duplicate_rank,
                           max("VersionNo") OVER (PARTITION BY "StoryId") AS maximum_version
                    FROM story_versions
                ), duplicate_versions AS (
                    SELECT "Id", "StoryId", maximum_version,
                           row_number() OVER (
                               PARTITION BY "StoryId"
                               ORDER BY "VersionNo", "Id") AS duplicate_offset
                    FROM marked_versions
                    WHERE duplicate_rank > 1
                )
                UPDATE story_versions AS version
                SET "VersionNo" = duplicate.maximum_version + duplicate.duplicate_offset
                FROM duplicate_versions AS duplicate
                WHERE version."Id" = duplicate."Id";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_story_versions_OutlineApprovedByUserId",
                table: "story_versions",
                column: "OutlineApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_story_versions_StoryId",
                table: "story_versions",
                column: "StoryId",
                unique: true,
                filter: "\"IsCurrent\" = true AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_story_versions_StoryId_VersionNo",
                table: "story_versions",
                columns: new[] { "StoryId", "VersionNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_story_generation_jobs_BaseStoryVersionId",
                table: "story_generation_jobs",
                column: "BaseStoryVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_story_generation_jobs_GenerationRequestId",
                table: "story_generation_jobs",
                column: "GenerationRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_story_generation_jobs_Operation_StoryVersionId",
                table: "story_generation_jobs",
                columns: new[] { "Operation", "StoryVersionId" },
                unique: true,
                filter: "\"StoryVersionId\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_story_generation_jobs_RequestedByUserId_Operation_Operation~",
                table: "story_generation_jobs",
                columns: new[] { "RequestedByUserId", "Operation", "OperationKey" },
                unique: true,
                filter: "\"RequestedByUserId\" IS NOT NULL AND \"OperationKey\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_story_generation_jobs_Status_Stage_LeaseExpiresAt",
                table: "story_generation_jobs",
                columns: new[] { "Status", "Stage", "LeaseExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_story_generation_jobs_StoryId",
                table: "story_generation_jobs",
                column: "StoryId",
                unique: true,
                filter: "\"Operation\" IN ('GenerateOutline', 'RegenerateOutline') AND \"Status\" IN ('Pending', 'Processing') AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_story_generation_jobs_StoryVersionId",
                table: "story_generation_jobs",
                column: "StoryVersionId");

            migrationBuilder.AddForeignKey(
                name: "FK_story_generation_jobs_story_generation_requests_GenerationR~",
                table: "story_generation_jobs",
                column: "GenerationRequestId",
                principalTable: "story_generation_requests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_story_generation_jobs_story_versions_BaseStoryVersionId",
                table: "story_generation_jobs",
                column: "BaseStoryVersionId",
                principalTable: "story_versions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_story_generation_jobs_story_versions_StoryVersionId",
                table: "story_generation_jobs",
                column: "StoryVersionId",
                principalTable: "story_versions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_story_generation_jobs_user_accounts_RequestedByUserId",
                table: "story_generation_jobs",
                column: "RequestedByUserId",
                principalTable: "user_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_story_versions_user_accounts_OutlineApprovedByUserId",
                table: "story_versions",
                column: "OutlineApprovedByUserId",
                principalTable: "user_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_story_generation_jobs_story_generation_requests_GenerationR~",
                table: "story_generation_jobs");

            migrationBuilder.DropForeignKey(
                name: "FK_story_generation_jobs_story_versions_BaseStoryVersionId",
                table: "story_generation_jobs");

            migrationBuilder.DropForeignKey(
                name: "FK_story_generation_jobs_story_versions_StoryVersionId",
                table: "story_generation_jobs");

            migrationBuilder.DropForeignKey(
                name: "FK_story_generation_jobs_user_accounts_RequestedByUserId",
                table: "story_generation_jobs");

            migrationBuilder.DropForeignKey(
                name: "FK_story_versions_user_accounts_OutlineApprovedByUserId",
                table: "story_versions");

            migrationBuilder.DropIndex(
                name: "IX_story_versions_OutlineApprovedByUserId",
                table: "story_versions");

            migrationBuilder.DropIndex(
                name: "IX_story_versions_StoryId",
                table: "story_versions");

            migrationBuilder.DropIndex(
                name: "IX_story_versions_StoryId_VersionNo",
                table: "story_versions");

            migrationBuilder.DropIndex(
                name: "IX_story_generation_jobs_BaseStoryVersionId",
                table: "story_generation_jobs");

            migrationBuilder.DropIndex(
                name: "IX_story_generation_jobs_GenerationRequestId",
                table: "story_generation_jobs");

            migrationBuilder.DropIndex(
                name: "IX_story_generation_jobs_Operation_StoryVersionId",
                table: "story_generation_jobs");

            migrationBuilder.DropIndex(
                name: "IX_story_generation_jobs_RequestedByUserId_Operation_Operation~",
                table: "story_generation_jobs");

            migrationBuilder.DropIndex(
                name: "IX_story_generation_jobs_Status_Stage_LeaseExpiresAt",
                table: "story_generation_jobs");

            migrationBuilder.DropIndex(
                name: "IX_story_generation_jobs_StoryId",
                table: "story_generation_jobs");

            migrationBuilder.DropIndex(
                name: "IX_story_generation_jobs_StoryVersionId",
                table: "story_generation_jobs");

            migrationBuilder.DropColumn(
                name: "OutlineApprovedAt",
                table: "story_versions");

            migrationBuilder.DropColumn(
                name: "OutlineApprovedByUserId",
                table: "story_versions");

            migrationBuilder.DropColumn(
                name: "AttemptNo",
                table: "story_generation_jobs");

            migrationBuilder.DropColumn(
                name: "BaseStoryVersionId",
                table: "story_generation_jobs");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "story_generation_jobs");

            migrationBuilder.DropColumn(
                name: "ErrorCode",
                table: "story_generation_jobs");

            migrationBuilder.DropColumn(
                name: "GenerationMetadataJson",
                table: "story_generation_jobs");

            migrationBuilder.DropColumn(
                name: "GenerationRequestId",
                table: "story_generation_jobs");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAt",
                table: "story_generation_jobs");

            migrationBuilder.DropColumn(
                name: "MaxAttempts",
                table: "story_generation_jobs");

            migrationBuilder.DropColumn(
                name: "Operation",
                table: "story_generation_jobs");

            migrationBuilder.DropColumn(
                name: "OperationKey",
                table: "story_generation_jobs");

            migrationBuilder.DropColumn(
                name: "RequestedByUserId",
                table: "story_generation_jobs");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "story_generation_jobs");

            migrationBuilder.DropColumn(
                name: "StoryVersionId",
                table: "story_generation_jobs");

            migrationBuilder.CreateIndex(
                name: "IX_story_versions_StoryId",
                table: "story_versions",
                column: "StoryId");

            migrationBuilder.CreateIndex(
                name: "IX_story_generation_jobs_StoryId",
                table: "story_generation_jobs",
                column: "StoryId");
        }
    }
}
