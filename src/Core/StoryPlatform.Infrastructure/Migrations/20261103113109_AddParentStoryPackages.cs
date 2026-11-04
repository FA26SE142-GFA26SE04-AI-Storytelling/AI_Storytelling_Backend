using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddParentStoryPackages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_story_generation_jobs_Operation_StoryVersionId",
                table: "story_generation_jobs");

            migrationBuilder.DropIndex(
                name: "IX_story_generation_jobs_StoryVersionId_ListeningInteractionTy~",
                table: "story_generation_jobs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Story_ScopeChild",
                table: "stories");

            migrationBuilder.DropIndex(
                name: "IX_media_contexts_StoryVersionId_Revision",
                table: "media_contexts");

            migrationBuilder.DropIndex(
                name: "IX_media_assets_IllustrationBeatId_Type",
                table: "media_assets");

            migrationBuilder.DropIndex(
                name: "IX_media_assets_StorySegmentId_Type",
                table: "media_assets");

            migrationBuilder.DropIndex(
                name: "IX_media_assets_StoryVocabularyId_Type",
                table: "media_assets");

            migrationBuilder.DropIndex(
                name: "IX_listening_interactions_StoryVersionId_AnchorOffset",
                table: "listening_interactions");

            migrationBuilder.AddColumn<int>(
                name: "ParentStoryPackageId",
                table: "story_vocabulary",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ParentStoryPackageId",
                table: "story_generation_jobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FamilyConfirmationId",
                table: "reading_sessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ParentStoryPackageId",
                table: "reading_sessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ParentStoryPackageId",
                table: "quiz_items",
                type: "integer",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ChildProfileId",
                table: "parent_story_operations",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "ParentStoryPackageId",
                table: "parent_story_operations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ParentStoryPackageId",
                table: "moderation_items",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ParentStoryPackageId",
                table: "media_contexts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ParentStoryPackageId",
                table: "media_assets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ParentStoryPackageId",
                table: "listening_interactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ParentStoryPackageId",
                table: "learning_artifact_generation_batches",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "parent_story_packages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoryId = table.Column<int>(type: "integer", nullable: false),
                    StoryVersionId = table.Column<int>(type: "integer", nullable: false),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    PreviousPackageId = table.Column<int>(type: "integer", nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SourceFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContextFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContextSnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    ComponentsJson = table.Column<string>(type: "jsonb", nullable: false),
                    GrayZoneAssessmentJson = table.Column<string>(type: "jsonb", nullable: false),
                    CheckResultJson = table.Column<string>(type: "jsonb", nullable: false),
                    ManifestJson = table.Column<string>(type: "jsonb", nullable: false),
                    LastErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ConfirmedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConfirmedByUserId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parent_story_packages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_parent_story_packages_child_profiles_ChildProfileId",
                        column: x => x.ChildProfileId,
                        principalTable: "child_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_parent_story_packages_parent_story_packages_PreviousPackage~",
                        column: x => x.PreviousPackageId,
                        principalTable: "parent_story_packages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_parent_story_packages_stories_StoryId",
                        column: x => x.StoryId,
                        principalTable: "stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_parent_story_packages_story_versions_StoryVersionId",
                        column: x => x.StoryVersionId,
                        principalTable: "story_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_parent_story_packages_user_accounts_ConfirmedByUserId",
                        column: x => x.ConfirmedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_parent_story_packages_user_accounts_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_story_vocabulary_ParentStoryPackageId",
                table: "story_vocabulary",
                column: "ParentStoryPackageId");

            migrationBuilder.CreateIndex(
                name: "IX_story_generation_jobs_Operation_StoryVersionId",
                table: "story_generation_jobs",
                columns: new[] { "Operation", "StoryVersionId" },
                unique: true,
                filter: "\"StoryVersionId\" IS NOT NULL AND \"IsDeleted\" = false AND \"ParentStoryPackageId\" IS NULL AND \"Operation\" <> 'GenerateListeningInteraction'");

            migrationBuilder.CreateIndex(
                name: "IX_story_generation_jobs_ParentStoryPackageId_Operation",
                table: "story_generation_jobs",
                columns: new[] { "ParentStoryPackageId", "Operation" },
                unique: true,
                filter: "\"ParentStoryPackageId\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_story_generation_jobs_StoryVersionId_ListeningInteractionTy~",
                table: "story_generation_jobs",
                columns: new[] { "StoryVersionId", "ListeningInteractionType" },
                unique: true,
                filter: "\"ParentStoryPackageId\" IS NULL AND \"Operation\" = 'GenerateListeningInteraction' AND \"StoryVersionId\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Story_ScopeChild",
                table: "stories",
                sql: "(\"Scope\" = 'Parent' AND ((\"ChildProfileId\" IS NOT NULL AND \"ChildProfileId\" > 0) OR (\"Source\" = 'Manual' AND \"ChildProfileId\" IS NULL AND \"CreateOperationId\" IS NOT NULL AND \"CreateOperationId\" <> ''))) OR (\"Scope\" = 'Creator' AND \"ChildProfileId\" IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_reading_sessions_FamilyConfirmationId",
                table: "reading_sessions",
                column: "FamilyConfirmationId");

            migrationBuilder.CreateIndex(
                name: "IX_reading_sessions_ParentStoryPackageId",
                table: "reading_sessions",
                column: "ParentStoryPackageId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_reading_sessions_parent_package_source",
                table: "reading_sessions",
                sql: "(\"ParentStoryPackageId\" IS NULL AND \"FamilyConfirmationId\" IS NULL) OR (\"ParentStoryPackageId\" IS NOT NULL AND \"FamilyConfirmationId\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_quiz_items_ParentStoryPackageId",
                table: "quiz_items",
                column: "ParentStoryPackageId");

            migrationBuilder.CreateIndex(
                name: "IX_parent_story_operations_ParentStoryPackageId",
                table: "parent_story_operations",
                column: "ParentStoryPackageId");

            migrationBuilder.CreateIndex(
                name: "IX_parent_story_operations_StoryId_ChildProfileId",
                table: "parent_story_operations",
                columns: new[] { "StoryId", "ChildProfileId" },
                unique: true,
                filter: "\"ParentStoryPackageId\" IS NOT NULL AND \"Kind\" = 'FamilyConfirmation' AND \"RevokedAt\" IS NULL AND \"IsDeleted\" = false");

            migrationBuilder.AddCheckConstraint(
                name: "CK_parent_grant_has_child",
                table: "parent_story_operations",
                sql: "\"Kind\" <> 'FamilyConfirmation' OR \"ChildProfileId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_moderation_items_ParentStoryPackageId",
                table: "moderation_items",
                column: "ParentStoryPackageId");

            migrationBuilder.CreateIndex(
                name: "IX_media_contexts_ParentStoryPackageId",
                table: "media_contexts",
                column: "ParentStoryPackageId",
                unique: true,
                filter: "\"ParentStoryPackageId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_media_contexts_StoryVersionId_Revision",
                table: "media_contexts",
                columns: new[] { "StoryVersionId", "Revision" },
                unique: true,
                filter: "\"ParentStoryPackageId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_IllustrationBeatId_Type",
                table: "media_assets",
                columns: new[] { "IllustrationBeatId", "Type" },
                unique: true,
                filter: "\"IllustrationBeatId\" IS NOT NULL AND \"IsDeleted\" = false AND \"IsCurrent\" = true AND \"ParentStoryPackageId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_ParentStoryPackageId_IllustrationBeatId_Type",
                table: "media_assets",
                columns: new[] { "ParentStoryPackageId", "IllustrationBeatId", "Type" },
                unique: true,
                filter: "\"ParentStoryPackageId\" IS NOT NULL AND \"IllustrationBeatId\" IS NOT NULL AND \"IsDeleted\" = false AND \"IsCurrent\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_ParentStoryPackageId_StorySegmentId_Type",
                table: "media_assets",
                columns: new[] { "ParentStoryPackageId", "StorySegmentId", "Type" },
                unique: true,
                filter: "\"ParentStoryPackageId\" IS NOT NULL AND \"StorySegmentId\" IS NOT NULL AND \"IsDeleted\" = false AND \"IsCurrent\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_StorySegmentId_Type",
                table: "media_assets",
                columns: new[] { "StorySegmentId", "Type" },
                unique: true,
                filter: "\"StorySegmentId\" IS NOT NULL AND \"IsDeleted\" = false AND \"IsCurrent\" = true AND \"ParentStoryPackageId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_StoryVocabularyId_Type",
                table: "media_assets",
                columns: new[] { "StoryVocabularyId", "Type" },
                unique: true,
                filter: "\"StoryVocabularyId\" IS NOT NULL AND \"IsDeleted\" = false AND \"IsCurrent\" = true AND \"ParentStoryPackageId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_listening_interactions_ParentStoryPackageId_AnchorOffset",
                table: "listening_interactions",
                columns: new[] { "ParentStoryPackageId", "AnchorOffset" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"ParentStoryPackageId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_listening_interactions_StoryVersionId_AnchorOffset",
                table: "listening_interactions",
                columns: new[] { "StoryVersionId", "AnchorOffset" },
                unique: true,
                filter: "\"IsDeleted\" = false AND \"ParentStoryPackageId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_learning_artifact_generation_batches_ParentStoryPackageId",
                table: "learning_artifact_generation_batches",
                column: "ParentStoryPackageId");

            migrationBuilder.CreateIndex(
                name: "IX_parent_story_packages_ChildProfileId",
                table: "parent_story_packages",
                column: "ChildProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_parent_story_packages_ConfirmedByUserId",
                table: "parent_story_packages",
                column: "ConfirmedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_parent_story_packages_CreatedByUserId",
                table: "parent_story_packages",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_parent_story_packages_PreviousPackageId",
                table: "parent_story_packages",
                column: "PreviousPackageId");

            migrationBuilder.CreateIndex(
                name: "IX_parent_story_packages_StoryId_Status",
                table: "parent_story_packages",
                columns: new[] { "StoryId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_parent_story_packages_StoryVersionId_ChildProfileId_Revision",
                table: "parent_story_packages",
                columns: new[] { "StoryVersionId", "ChildProfileId", "Revision" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_learning_artifact_generation_batches_parent_story_packages_~",
                table: "learning_artifact_generation_batches",
                column: "ParentStoryPackageId",
                principalTable: "parent_story_packages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_listening_interactions_parent_story_packages_ParentStoryPac~",
                table: "listening_interactions",
                column: "ParentStoryPackageId",
                principalTable: "parent_story_packages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_media_assets_parent_story_packages_ParentStoryPackageId",
                table: "media_assets",
                column: "ParentStoryPackageId",
                principalTable: "parent_story_packages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_media_contexts_parent_story_packages_ParentStoryPackageId",
                table: "media_contexts",
                column: "ParentStoryPackageId",
                principalTable: "parent_story_packages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_moderation_items_parent_story_packages_ParentStoryPackageId",
                table: "moderation_items",
                column: "ParentStoryPackageId",
                principalTable: "parent_story_packages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_parent_story_operations_parent_story_packages_ParentStoryPa~",
                table: "parent_story_operations",
                column: "ParentStoryPackageId",
                principalTable: "parent_story_packages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_quiz_items_parent_story_packages_ParentStoryPackageId",
                table: "quiz_items",
                column: "ParentStoryPackageId",
                principalTable: "parent_story_packages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_reading_sessions_parent_story_operations_FamilyConfirmation~",
                table: "reading_sessions",
                column: "FamilyConfirmationId",
                principalTable: "parent_story_operations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_reading_sessions_parent_story_packages_ParentStoryPackageId",
                table: "reading_sessions",
                column: "ParentStoryPackageId",
                principalTable: "parent_story_packages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_story_generation_jobs_parent_story_packages_ParentStoryPack~",
                table: "story_generation_jobs",
                column: "ParentStoryPackageId",
                principalTable: "parent_story_packages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_story_vocabulary_parent_story_packages_ParentStoryPackageId",
                table: "story_vocabulary",
                column: "ParentStoryPackageId",
                principalTable: "parent_story_packages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_learning_artifact_generation_batches_parent_story_packages_~",
                table: "learning_artifact_generation_batches");

            migrationBuilder.DropForeignKey(
                name: "FK_listening_interactions_parent_story_packages_ParentStoryPac~",
                table: "listening_interactions");

            migrationBuilder.DropForeignKey(
                name: "FK_media_assets_parent_story_packages_ParentStoryPackageId",
                table: "media_assets");

            migrationBuilder.DropForeignKey(
                name: "FK_media_contexts_parent_story_packages_ParentStoryPackageId",
                table: "media_contexts");

            migrationBuilder.DropForeignKey(
                name: "FK_moderation_items_parent_story_packages_ParentStoryPackageId",
                table: "moderation_items");

            migrationBuilder.DropForeignKey(
                name: "FK_parent_story_operations_parent_story_packages_ParentStoryPa~",
                table: "parent_story_operations");

            migrationBuilder.DropForeignKey(
                name: "FK_quiz_items_parent_story_packages_ParentStoryPackageId",
                table: "quiz_items");

            migrationBuilder.DropForeignKey(
                name: "FK_reading_sessions_parent_story_operations_FamilyConfirmation~",
                table: "reading_sessions");

            migrationBuilder.DropForeignKey(
                name: "FK_reading_sessions_parent_story_packages_ParentStoryPackageId",
                table: "reading_sessions");

            migrationBuilder.DropForeignKey(
                name: "FK_story_generation_jobs_parent_story_packages_ParentStoryPack~",
                table: "story_generation_jobs");

            migrationBuilder.DropForeignKey(
                name: "FK_story_vocabulary_parent_story_packages_ParentStoryPackageId",
                table: "story_vocabulary");

            migrationBuilder.DropTable(
                name: "parent_story_packages");

            migrationBuilder.DropIndex(
                name: "IX_story_vocabulary_ParentStoryPackageId",
                table: "story_vocabulary");

            migrationBuilder.DropIndex(
                name: "IX_story_generation_jobs_Operation_StoryVersionId",
                table: "story_generation_jobs");

            migrationBuilder.DropIndex(
                name: "IX_story_generation_jobs_ParentStoryPackageId_Operation",
                table: "story_generation_jobs");

            migrationBuilder.DropIndex(
                name: "IX_story_generation_jobs_StoryVersionId_ListeningInteractionTy~",
                table: "story_generation_jobs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Story_ScopeChild",
                table: "stories");

            migrationBuilder.DropIndex(
                name: "IX_reading_sessions_FamilyConfirmationId",
                table: "reading_sessions");

            migrationBuilder.DropIndex(
                name: "IX_reading_sessions_ParentStoryPackageId",
                table: "reading_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_reading_sessions_parent_package_source",
                table: "reading_sessions");

            migrationBuilder.DropIndex(
                name: "IX_quiz_items_ParentStoryPackageId",
                table: "quiz_items");

            migrationBuilder.DropIndex(
                name: "IX_parent_story_operations_ParentStoryPackageId",
                table: "parent_story_operations");

            migrationBuilder.DropIndex(
                name: "IX_parent_story_operations_StoryId_ChildProfileId",
                table: "parent_story_operations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_parent_grant_has_child",
                table: "parent_story_operations");

            migrationBuilder.DropIndex(
                name: "IX_moderation_items_ParentStoryPackageId",
                table: "moderation_items");

            migrationBuilder.DropIndex(
                name: "IX_media_contexts_ParentStoryPackageId",
                table: "media_contexts");

            migrationBuilder.DropIndex(
                name: "IX_media_contexts_StoryVersionId_Revision",
                table: "media_contexts");

            migrationBuilder.DropIndex(
                name: "IX_media_assets_IllustrationBeatId_Type",
                table: "media_assets");

            migrationBuilder.DropIndex(
                name: "IX_media_assets_ParentStoryPackageId_IllustrationBeatId_Type",
                table: "media_assets");

            migrationBuilder.DropIndex(
                name: "IX_media_assets_ParentStoryPackageId_StorySegmentId_Type",
                table: "media_assets");

            migrationBuilder.DropIndex(
                name: "IX_media_assets_StorySegmentId_Type",
                table: "media_assets");

            migrationBuilder.DropIndex(
                name: "IX_media_assets_StoryVocabularyId_Type",
                table: "media_assets");

            migrationBuilder.DropIndex(
                name: "IX_listening_interactions_ParentStoryPackageId_AnchorOffset",
                table: "listening_interactions");

            migrationBuilder.DropIndex(
                name: "IX_listening_interactions_StoryVersionId_AnchorOffset",
                table: "listening_interactions");

            migrationBuilder.DropIndex(
                name: "IX_learning_artifact_generation_batches_ParentStoryPackageId",
                table: "learning_artifact_generation_batches");

            migrationBuilder.DropColumn(
                name: "ParentStoryPackageId",
                table: "story_vocabulary");

            migrationBuilder.DropColumn(
                name: "ParentStoryPackageId",
                table: "story_generation_jobs");

            migrationBuilder.DropColumn(
                name: "FamilyConfirmationId",
                table: "reading_sessions");

            migrationBuilder.DropColumn(
                name: "ParentStoryPackageId",
                table: "reading_sessions");

            migrationBuilder.DropColumn(
                name: "ParentStoryPackageId",
                table: "quiz_items");

            migrationBuilder.DropColumn(
                name: "ParentStoryPackageId",
                table: "parent_story_operations");

            migrationBuilder.DropColumn(
                name: "ParentStoryPackageId",
                table: "moderation_items");

            migrationBuilder.DropColumn(
                name: "ParentStoryPackageId",
                table: "media_contexts");

            migrationBuilder.DropColumn(
                name: "ParentStoryPackageId",
                table: "media_assets");

            migrationBuilder.DropColumn(
                name: "ParentStoryPackageId",
                table: "listening_interactions");

            migrationBuilder.DropColumn(
                name: "ParentStoryPackageId",
                table: "learning_artifact_generation_batches");

            migrationBuilder.AlterColumn<int>(
                name: "ChildProfileId",
                table: "parent_story_operations",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_story_generation_jobs_Operation_StoryVersionId",
                table: "story_generation_jobs",
                columns: new[] { "Operation", "StoryVersionId" },
                unique: true,
                filter: "\"StoryVersionId\" IS NOT NULL AND \"IsDeleted\" = false AND \"Operation\" <> 'GenerateListeningInteraction'");

            migrationBuilder.CreateIndex(
                name: "IX_story_generation_jobs_StoryVersionId_ListeningInteractionTy~",
                table: "story_generation_jobs",
                columns: new[] { "StoryVersionId", "ListeningInteractionType" },
                unique: true,
                filter: "\"Operation\" = 'GenerateListeningInteraction' AND \"StoryVersionId\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Story_ScopeChild",
                table: "stories",
                sql: "(\"Scope\" = 'Parent' AND \"ChildProfileId\" IS NOT NULL AND \"ChildProfileId\" > 0) OR (\"Scope\" = 'Creator' AND \"ChildProfileId\" IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_media_contexts_StoryVersionId_Revision",
                table: "media_contexts",
                columns: new[] { "StoryVersionId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_IllustrationBeatId_Type",
                table: "media_assets",
                columns: new[] { "IllustrationBeatId", "Type" },
                unique: true,
                filter: "\"IllustrationBeatId\" IS NOT NULL AND \"IsDeleted\" = false AND \"IsCurrent\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_StorySegmentId_Type",
                table: "media_assets",
                columns: new[] { "StorySegmentId", "Type" },
                unique: true,
                filter: "\"StorySegmentId\" IS NOT NULL AND \"IsDeleted\" = false AND \"IsCurrent\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_StoryVocabularyId_Type",
                table: "media_assets",
                columns: new[] { "StoryVocabularyId", "Type" },
                unique: true,
                filter: "\"StoryVocabularyId\" IS NOT NULL AND \"IsDeleted\" = false AND \"IsCurrent\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_listening_interactions_StoryVersionId_AnchorOffset",
                table: "listening_interactions",
                columns: new[] { "StoryVersionId", "AnchorOffset" },
                unique: true,
                filter: "\"IsDeleted\" = false");
        }
    }
}
