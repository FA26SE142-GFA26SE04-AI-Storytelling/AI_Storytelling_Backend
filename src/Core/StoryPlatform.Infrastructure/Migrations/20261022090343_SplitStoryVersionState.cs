using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SplitStoryVersionState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_story_versions_user_accounts_GrayZoneClearedByUserId",
                table: "story_versions");

            migrationBuilder.DropForeignKey(
                name: "FK_story_versions_user_accounts_ReviewedByUserId",
                table: "story_versions");

            migrationBuilder.DropIndex(
                name: "IX_story_versions_GrayZoneClearedByUserId",
                table: "story_versions");

            migrationBuilder.DropIndex(
                name: "IX_story_versions_ReviewedByUserId",
                table: "story_versions");

            migrationBuilder.DropIndex(
                name: "IX_story_versions_StoryId_ImportPreviewHash",
                table: "story_versions");

            migrationBuilder.DropIndex(
                name: "IX_story_versions_StoryId_OperationId",
                table: "story_versions");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_story_versions_Id_StoryId",
                table: "story_versions",
                columns: new[] { "Id", "StoryId" });

            migrationBuilder.CreateTable(
                name: "story_authoring_operations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoryId = table.Column<int>(type: "integer", nullable: false),
                    StoryVersionId = table.Column<int>(type: "integer", nullable: false),
                    ActorUserId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    OperationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OperationResponseJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_story_authoring_operations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_story_authoring_operations_story_versions_StoryVersionId_St~",
                        columns: x => new { x.StoryVersionId, x.StoryId },
                        principalTable: "story_versions",
                        principalColumns: new[] { "Id", "StoryId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_story_authoring_operations_user_accounts_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "story_version_authoring_metadata",
                columns: table => new
                {
                    StoryVersionId = table.Column<int>(type: "integer", nullable: false),
                    StoryId = table.Column<int>(type: "integer", nullable: false),
                    AuthoringOrigin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    AssistanceProvenanceJson = table.Column<string>(type: "jsonb", nullable: true),
                    SourceDeclaration = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    RightsDeclaration = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ImportFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ImportSourceHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ImportPreviewHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_story_version_authoring_metadata", x => x.StoryVersionId);
                    table.ForeignKey(
                        name: "FK_story_version_authoring_metadata_stories_StoryId",
                        column: x => x.StoryId,
                        principalTable: "stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_story_version_authoring_metadata_story_versions_StoryVersio~",
                        column: x => x.StoryVersionId,
                        principalTable: "story_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "story_version_media_states",
                columns: table => new
                {
                    StoryVersionId = table.Column<int>(type: "integer", nullable: false),
                    MediaPackageRevision = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    ConfirmedMediaPackageRevision = table.Column<int>(type: "integer", nullable: true),
                    MediaConfirmedByUserId = table.Column<int>(type: "integer", nullable: true),
                    MediaConfirmedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_story_version_media_states", x => x.StoryVersionId);
                    table.ForeignKey(
                        name: "FK_story_version_media_states_story_versions_StoryVersionId",
                        column: x => x.StoryVersionId,
                        principalTable: "story_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_story_version_media_states_user_accounts_MediaConfirmedByUs~",
                        column: x => x.MediaConfirmedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "story_version_moderation_states",
                columns: table => new
                {
                    StoryVersionId = table.Column<int>(type: "integer", nullable: false),
                    ModerationZone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    GrayZoneFlags = table.Column<string>(type: "jsonb", nullable: true),
                    GrayZoneCleared = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    GrayZoneClearedByUserId = table.Column<int>(type: "integer", nullable: true),
                    GrayZoneClearedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "None"),
                    ReviewSubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewedByUserId = table.Column<int>(type: "integer", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewFeedback = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_story_version_moderation_states", x => x.StoryVersionId);
                    table.ForeignKey(
                        name: "FK_story_version_moderation_states_story_versions_StoryVersion~",
                        column: x => x.StoryVersionId,
                        principalTable: "story_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_story_version_moderation_states_user_accounts_GrayZoneClear~",
                        column: x => x.GrayZoneClearedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_story_version_moderation_states_user_accounts_ReviewedByUse~",
                        column: x => x.ReviewedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_story_authoring_operations_ActorUserId",
                table: "story_authoring_operations",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_story_authoring_operations_StoryId_OperationId",
                table: "story_authoring_operations",
                columns: new[] { "StoryId", "OperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_story_authoring_operations_StoryVersionId_StoryId",
                table: "story_authoring_operations",
                columns: new[] { "StoryVersionId", "StoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_story_version_authoring_metadata_StoryId_ImportPreviewHash",
                table: "story_version_authoring_metadata",
                columns: new[] { "StoryId", "ImportPreviewHash" },
                unique: true,
                filter: "\"ImportPreviewHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_story_version_media_states_MediaConfirmedByUserId",
                table: "story_version_media_states",
                column: "MediaConfirmedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_story_version_moderation_states_GrayZoneClearedByUserId",
                table: "story_version_moderation_states",
                column: "GrayZoneClearedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_story_version_moderation_states_ReviewedByUserId",
                table: "story_version_moderation_states",
                column: "ReviewedByUserId");

            // Copy every version, including soft-deleted owners, before removing legacy fields.
            migrationBuilder.Sql("""
                INSERT INTO story_version_authoring_metadata
                    ("StoryVersionId", "StoryId", "AuthoringOrigin", "AssistanceProvenanceJson",
                     "SourceDeclaration", "RightsDeclaration", "ImportFileName", "ImportSourceHash", "ImportPreviewHash")
                SELECT "Id", "StoryId", "AuthoringOrigin", "AssistanceProvenanceJson",
                       "SourceDeclaration", "RightsDeclaration", "ImportFileName", "ImportSourceHash", "ImportPreviewHash"
                FROM story_versions;

                INSERT INTO story_version_moderation_states
                    ("StoryVersionId", "ModerationZone", "GrayZoneFlags", "GrayZoneCleared",
                     "GrayZoneClearedByUserId", "GrayZoneClearedAt", "ReviewStatus", "ReviewSubmittedAt",
                     "ReviewedByUserId", "ReviewedAt", "ReviewFeedback")
                SELECT "Id", "ModerationZone", "GrayZoneFlags", "GrayZoneCleared",
                       "GrayZoneClearedByUserId", "GrayZoneClearedAt", "ReviewStatus", "ReviewSubmittedAt",
                       "ReviewedByUserId", "ReviewedAt", "ReviewFeedback"
                FROM story_versions;

                -- Official baseline has no media confirmation columns. JSON lookup also preserves
                -- their values if a deployment already added them; absent keys become SQL NULL.
                INSERT INTO story_version_media_states
                    ("StoryVersionId", "MediaPackageRevision", "ConfirmedMediaPackageRevision",
                     "MediaConfirmedByUserId", "MediaConfirmedAt")
                SELECT v."Id", COALESCE((to_jsonb(v)->>'MediaPackageRevision')::integer, 0),
                       (to_jsonb(v)->>'ConfirmedMediaPackageRevision')::integer,
                       (to_jsonb(v)->>'MediaConfirmedByUserId')::integer,
                       (to_jsonb(v)->>'MediaConfirmedAt')::timestamptz
                FROM story_versions v;

                INSERT INTO story_authoring_operations
                    ("StoryId", "StoryVersionId", "ActorUserId", "Kind", "OperationId", "RequestHash",
                     "OperationResponseJson", "CreatedAt", "UpdatedAt", "IsDeleted")
                SELECT v."StoryId", v."Id", COALESCE(v."EditorUserId", s."AuthorUserId"),
                       CASE v."AuthoringOrigin" WHEN 'Import' THEN 'confirm-import'
                            WHEN 'Restore' THEN 'restore' ELSE 'save-manual' END,
                       v."OperationId", v."RequestHash", v."OperationResponseJson",
                       v."CreatedAt", v."UpdatedAt", v."IsDeleted"
                FROM story_versions v JOIN stories s ON s."Id" = v."StoryId"
                WHERE v."OperationId" IS NOT NULL;

                -- Fail atomically rather than accepting an incomplete backfill.
                DO $backfill$
                BEGIN
                    IF (SELECT count(*) FROM story_versions) <> (SELECT count(*) FROM story_version_authoring_metadata)
                       OR (SELECT count(*) FROM story_versions) <> (SELECT count(*) FROM story_version_moderation_states)
                       OR (SELECT count(*) FROM story_versions) <> (SELECT count(*) FROM story_version_media_states)
                       OR (SELECT count(*) FROM story_versions WHERE "OperationId" IS NOT NULL)
                          <> (SELECT count(*) FROM story_authoring_operations) THEN
                        RAISE EXCEPTION 'SplitStoryVersionState: backfill row counts do not match';
                    END IF;
                END;
                $backfill$;
                """);

            migrationBuilder.DropColumn(
                name: "AssistanceProvenanceJson",
                table: "story_versions");

            migrationBuilder.DropColumn(
                name: "AuthoringOrigin",
                table: "story_versions");

            migrationBuilder.DropColumn(
                name: "GrayZoneCleared",
                table: "story_versions");

            migrationBuilder.DropColumn(
                name: "GrayZoneClearedAt",
                table: "story_versions");

            migrationBuilder.DropColumn(
                name: "GrayZoneClearedByUserId",
                table: "story_versions");

            migrationBuilder.DropColumn(
                name: "GrayZoneFlags",
                table: "story_versions");

            migrationBuilder.DropColumn(
                name: "ImportFileName",
                table: "story_versions");

            migrationBuilder.DropColumn(
                name: "ImportPreviewHash",
                table: "story_versions");

            migrationBuilder.DropColumn(
                name: "ImportSourceHash",
                table: "story_versions");

            migrationBuilder.DropColumn(
                name: "ModerationZone",
                table: "story_versions");

            migrationBuilder.DropColumn(
                name: "OperationId",
                table: "story_versions");

            migrationBuilder.DropColumn(
                name: "OperationResponseJson",
                table: "story_versions");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "story_versions");

            migrationBuilder.DropColumn(
                name: "ReviewFeedback",
                table: "story_versions");

            migrationBuilder.DropColumn(
                name: "ReviewStatus",
                table: "story_versions");

            migrationBuilder.DropColumn(
                name: "ReviewSubmittedAt",
                table: "story_versions");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "story_versions");

            migrationBuilder.DropColumn(
                name: "ReviewedByUserId",
                table: "story_versions");

            migrationBuilder.DropColumn(
                name: "RightsDeclaration",
                table: "story_versions");

            migrationBuilder.DropColumn(
                name: "SourceDeclaration",
                table: "story_versions");

            migrationBuilder.Sql("""
                ALTER TABLE story_versions
                    DROP COLUMN IF EXISTS "MediaPackageRevision",
                    DROP COLUMN IF EXISTS "ConfirmedMediaPackageRevision",
                    DROP COLUMN IF EXISTS "MediaConfirmedByUserId",
                    DROP COLUMN IF EXISTS "MediaConfirmedAt";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The old schema has one receipt per version and no package confirmation state.
            // Reject an irreversible downgrade instead of silently discarding new data.
            migrationBuilder.Sql("""
                DO $rollback$
                BEGIN
                    IF EXISTS (SELECT 1 FROM story_authoring_operations GROUP BY "StoryVersionId" HAVING count(*) > 1)
                       OR EXISTS (SELECT 1 FROM story_version_media_states
                                  WHERE "MediaPackageRevision" <> 0 OR "ConfirmedMediaPackageRevision" IS NOT NULL
                                     OR "MediaConfirmedByUserId" IS NOT NULL OR "MediaConfirmedAt" IS NOT NULL) THEN
                        RAISE EXCEPTION 'SplitStoryVersionState: rollback would lose receipts or media confirmation; keep the split schema';
                    END IF;
                END;
                $rollback$;
                """);

            migrationBuilder.AddColumn<string>(
                name: "AssistanceProvenanceJson",
                table: "story_versions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AuthoringOrigin",
                table: "story_versions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "GrayZoneCleared",
                table: "story_versions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "GrayZoneClearedAt",
                table: "story_versions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GrayZoneClearedByUserId",
                table: "story_versions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GrayZoneFlags",
                table: "story_versions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImportFileName",
                table: "story_versions",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImportPreviewHash",
                table: "story_versions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImportSourceHash",
                table: "story_versions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModerationZone",
                table: "story_versions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperationId",
                table: "story_versions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperationResponseJson",
                table: "story_versions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "story_versions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewFeedback",
                table: "story_versions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewStatus",
                table: "story_versions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewSubmittedAt",
                table: "story_versions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAt",
                table: "story_versions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReviewedByUserId",
                table: "story_versions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RightsDeclaration",
                table: "story_versions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SourceDeclaration",
                table: "story_versions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE story_versions AS v SET
                    "AuthoringOrigin" = m."AuthoringOrigin", "AssistanceProvenanceJson" = m."AssistanceProvenanceJson",
                    "SourceDeclaration" = m."SourceDeclaration", "RightsDeclaration" = m."RightsDeclaration",
                    "ImportFileName" = m."ImportFileName", "ImportSourceHash" = m."ImportSourceHash",
                    "ImportPreviewHash" = m."ImportPreviewHash"
                FROM story_version_authoring_metadata m WHERE m."StoryVersionId" = v."Id";

                UPDATE story_versions AS v SET
                    "ModerationZone" = m."ModerationZone", "GrayZoneFlags" = m."GrayZoneFlags",
                    "GrayZoneCleared" = m."GrayZoneCleared", "GrayZoneClearedByUserId" = m."GrayZoneClearedByUserId",
                    "GrayZoneClearedAt" = m."GrayZoneClearedAt", "ReviewStatus" = m."ReviewStatus",
                    "ReviewSubmittedAt" = m."ReviewSubmittedAt", "ReviewedByUserId" = m."ReviewedByUserId",
                    "ReviewedAt" = m."ReviewedAt", "ReviewFeedback" = m."ReviewFeedback"
                FROM story_version_moderation_states m WHERE m."StoryVersionId" = v."Id";

                UPDATE story_versions AS v SET
                    "OperationId" = o."OperationId", "RequestHash" = o."RequestHash",
                    "OperationResponseJson" = o."OperationResponseJson"
                FROM story_authoring_operations o WHERE o."StoryVersionId" = v."Id";
                """);

            migrationBuilder.DropTable(
                name: "story_authoring_operations");

            migrationBuilder.DropTable(
                name: "story_version_authoring_metadata");

            migrationBuilder.DropTable(
                name: "story_version_media_states");

            migrationBuilder.DropTable(
                name: "story_version_moderation_states");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_story_versions_Id_StoryId",
                table: "story_versions");

            migrationBuilder.CreateIndex(
                name: "IX_story_versions_GrayZoneClearedByUserId",
                table: "story_versions",
                column: "GrayZoneClearedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_story_versions_ReviewedByUserId",
                table: "story_versions",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_story_versions_StoryId_ImportPreviewHash",
                table: "story_versions",
                columns: new[] { "StoryId", "ImportPreviewHash" },
                unique: true,
                filter: "\"ImportPreviewHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_story_versions_StoryId_OperationId",
                table: "story_versions",
                columns: new[] { "StoryId", "OperationId" },
                unique: true,
                filter: "\"OperationId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_story_versions_user_accounts_GrayZoneClearedByUserId",
                table: "story_versions",
                column: "GrayZoneClearedByUserId",
                principalTable: "user_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_story_versions_user_accounts_ReviewedByUserId",
                table: "story_versions",
                column: "ReviewedByUserId",
                principalTable: "user_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
