using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SplitPhaseFiveMediaJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Do not guess which legacy asset is selected if constraints were bypassed.
            // This runs before the original uniqueness indexes are replaced.
            migrationBuilder.Sql("""
                DO $legacy_assets$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM media_assets
                        WHERE "IsDeleted" = false AND "IllustrationBeatId" IS NOT NULL
                        GROUP BY "IllustrationBeatId", "Type" HAVING count(*) > 1
                    ) OR EXISTS (
                        SELECT 1 FROM media_assets
                        WHERE "IsDeleted" = false AND "StorySegmentId" IS NOT NULL
                        GROUP BY "StorySegmentId", "Type" HAVING count(*) > 1
                    ) OR EXISTS (
                        SELECT 1 FROM media_assets
                        WHERE "IsDeleted" = false AND "Type" = 'Illustration'
                          AND "StorySceneId" IS NOT NULL AND "IllustrationBeatId" IS NULL
                        GROUP BY "StoryVersionId", "StorySceneId" HAVING count(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'SplitPhaseFiveMediaJobs: ambiguous legacy media; select the correct asset before migrating';
                    END IF;
                END;
                $legacy_assets$;
                """);

            migrationBuilder.DropIndex(
                name: "IX_media_assets_IllustrationBeatId_Type",
                table: "media_assets");

            migrationBuilder.DropIndex(
                name: "IX_media_assets_StorySegmentId_Type",
                table: "media_assets");

            migrationBuilder.DropIndex(
                name: "IX_media_assets_StoryVersionId",
                table: "media_assets");

            migrationBuilder.AddColumn<int>(
                name: "MediaContextId",
                table: "story_generation_jobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MediaPackageRevision",
                table: "story_generation_jobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptAt",
                table: "story_generation_jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ParentJobId",
                table: "story_generation_jobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCurrent",
                table: "media_assets",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReplacementResolvedAt",
                table: "media_assets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReplacesMediaAssetId",
                table: "media_assets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequestedPackageRevision",
                table: "media_assets",
                type: "integer",
                nullable: true);

            // ADD IsCurrent DEFAULT true preserves the selected live assets and old URLs.
            // NULL RequestedPackageRevision intentionally identifies legacy media. No job,
            // package confirmation, file, URL or provider metadata is reset by this migration.
            migrationBuilder.Sql("""
                UPDATE media_assets SET "IsCurrent" = false WHERE "IsDeleted" = true;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_story_generation_jobs_MediaContextId",
                table: "story_generation_jobs",
                column: "MediaContextId");

            migrationBuilder.CreateIndex(
                name: "IX_story_generation_jobs_ParentJobId_Status_NextAttemptAt",
                table: "story_generation_jobs",
                columns: new[] { "ParentJobId", "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_IllustrationBeatId_Type",
                table: "media_assets",
                columns: new[] { "IllustrationBeatId", "Type" },
                unique: true,
                filter: "\"IllustrationBeatId\" IS NOT NULL AND \"IsDeleted\" = false AND \"IsCurrent\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_ReplacesMediaAssetId",
                table: "media_assets",
                column: "ReplacesMediaAssetId",
                unique: true,
                filter: "\"ReplacesMediaAssetId\" IS NOT NULL AND \"ReplacementResolvedAt\" IS NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_StorySegmentId_Type",
                table: "media_assets",
                columns: new[] { "StorySegmentId", "Type" },
                unique: true,
                filter: "\"StorySegmentId\" IS NOT NULL AND \"IsDeleted\" = false AND \"IsCurrent\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_StoryVersionId_RequestedPackageRevision",
                table: "media_assets",
                columns: new[] { "StoryVersionId", "RequestedPackageRevision" });

            migrationBuilder.AddForeignKey(
                name: "FK_media_assets_media_assets_ReplacesMediaAssetId",
                table: "media_assets",
                column: "ReplacesMediaAssetId",
                principalTable: "media_assets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_story_generation_jobs_media_contexts_MediaContextId",
                table: "story_generation_jobs",
                column: "MediaContextId",
                principalTable: "media_contexts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_story_generation_jobs_story_generation_jobs_ParentJobId",
                table: "story_generation_jobs",
                column: "ParentJobId",
                principalTable: "story_generation_jobs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The old worker cannot represent parallel jobs or replacement history.
            // Reject a lossy rollback instead of deleting candidates or picking a winner.
            migrationBuilder.Sql("""
                DO $rollback$
                BEGIN
                    IF EXISTS (SELECT 1 FROM story_generation_jobs
                               WHERE "ParentJobId" IS NOT NULL OR "MediaContextId" IS NOT NULL
                                  OR "MediaPackageRevision" IS NOT NULL OR "NextAttemptAt" IS NOT NULL)
                       OR EXISTS (SELECT 1 FROM media_assets
                                  WHERE "ReplacesMediaAssetId" IS NOT NULL OR "RequestedPackageRevision" IS NOT NULL
                                     OR "ReplacementResolvedAt" IS NOT NULL OR ("IsCurrent" = false AND "IsDeleted" = false)) THEN
                        RAISE EXCEPTION 'SplitPhaseFiveMediaJobs: rollback would lose media jobs or replacement history; keep the split schema';
                    END IF;
                END;
                $rollback$;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_media_assets_media_assets_ReplacesMediaAssetId",
                table: "media_assets");

            migrationBuilder.DropForeignKey(
                name: "FK_story_generation_jobs_media_contexts_MediaContextId",
                table: "story_generation_jobs");

            migrationBuilder.DropForeignKey(
                name: "FK_story_generation_jobs_story_generation_jobs_ParentJobId",
                table: "story_generation_jobs");

            migrationBuilder.DropIndex(
                name: "IX_story_generation_jobs_MediaContextId",
                table: "story_generation_jobs");

            migrationBuilder.DropIndex(
                name: "IX_story_generation_jobs_ParentJobId_Status_NextAttemptAt",
                table: "story_generation_jobs");

            migrationBuilder.DropIndex(
                name: "IX_media_assets_IllustrationBeatId_Type",
                table: "media_assets");

            migrationBuilder.DropIndex(
                name: "IX_media_assets_ReplacesMediaAssetId",
                table: "media_assets");

            migrationBuilder.DropIndex(
                name: "IX_media_assets_StorySegmentId_Type",
                table: "media_assets");

            migrationBuilder.DropIndex(
                name: "IX_media_assets_StoryVersionId_RequestedPackageRevision",
                table: "media_assets");

            migrationBuilder.DropColumn(
                name: "MediaContextId",
                table: "story_generation_jobs");

            migrationBuilder.DropColumn(
                name: "MediaPackageRevision",
                table: "story_generation_jobs");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "story_generation_jobs");

            migrationBuilder.DropColumn(
                name: "ParentJobId",
                table: "story_generation_jobs");

            migrationBuilder.DropColumn(
                name: "IsCurrent",
                table: "media_assets");

            migrationBuilder.DropColumn(
                name: "ReplacementResolvedAt",
                table: "media_assets");

            migrationBuilder.DropColumn(
                name: "ReplacesMediaAssetId",
                table: "media_assets");

            migrationBuilder.DropColumn(
                name: "RequestedPackageRevision",
                table: "media_assets");

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_IllustrationBeatId_Type",
                table: "media_assets",
                columns: new[] { "IllustrationBeatId", "Type" },
                unique: true,
                filter: "\"IllustrationBeatId\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_StorySegmentId_Type",
                table: "media_assets",
                columns: new[] { "StorySegmentId", "Type" },
                unique: true,
                filter: "\"StorySegmentId\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_StoryVersionId",
                table: "media_assets",
                column: "StoryVersionId");
        }
    }
}
