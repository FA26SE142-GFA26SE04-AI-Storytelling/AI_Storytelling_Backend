using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaAssetTasksAndLearningArtifactBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Example",
                table: "story_vocabulary",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceEnd",
                table: "story_vocabulary",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceQuote",
                table: "story_vocabulary",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceStart",
                table: "story_vocabulary",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Explanation",
                table: "quiz_items",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Hint",
                table: "quiz_items",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LearningGoal",
                table: "quiz_items",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OptionFeedbackJson",
                table: "quiz_items",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceEnd",
                table: "quiz_items",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceQuote",
                table: "quiz_items",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceStart",
                table: "quiz_items",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "learning_artifact_generation_batches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoryId = table.Column<int>(type: "integer", nullable: false),
                    StoryVersionId = table.Column<int>(type: "integer", nullable: false),
                    RootJobId = table.Column<int>(type: "integer", nullable: false),
                    RequestedByUserId = table.Column<int>(type: "integer", nullable: false),
                    SourceFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContextFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RootClaimToken = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    BatchNumber = table.Column<int>(type: "integer", nullable: false),
                    TargetCount = table.Column<int>(type: "integer", nullable: false),
                    RequestJson = table.Column<string>(type: "jsonb", nullable: false),
                    ResultJson = table.Column<string>(type: "jsonb", nullable: true),
                    ShortfallReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LeaseExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_learning_artifact_generation_batches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_learning_artifact_generation_batches_stories_StoryId",
                        column: x => x.StoryId,
                        principalTable: "stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_learning_artifact_generation_batches_story_generation_jobs_~",
                        column: x => x.RootJobId,
                        principalTable: "story_generation_jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_learning_artifact_generation_batches_story_versions_StoryVe~",
                        column: x => x.StoryVersionId,
                        principalTable: "story_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_learning_artifact_generation_batches_user_accounts_Requeste~",
                        column: x => x.RequestedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "media_asset_generation_tasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoryId = table.Column<int>(type: "integer", nullable: false),
                    StoryVersionId = table.Column<int>(type: "integer", nullable: false),
                    BranchJobId = table.Column<int>(type: "integer", nullable: false),
                    MediaAssetId = table.Column<int>(type: "integer", nullable: false),
                    IllustrationBeatId = table.Column<int>(type: "integer", nullable: false),
                    MediaContextId = table.Column<int>(type: "integer", nullable: false),
                    PackageRevision = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AttemptNo = table.Column<int>(type: "integer", nullable: false),
                    QueuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LeaseExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_asset_generation_tasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_media_asset_generation_tasks_illustration_beats_Illustratio~",
                        column: x => x.IllustrationBeatId,
                        principalTable: "illustration_beats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_media_asset_generation_tasks_media_assets_MediaAssetId",
                        column: x => x.MediaAssetId,
                        principalTable: "media_assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_media_asset_generation_tasks_media_contexts_MediaContextId",
                        column: x => x.MediaContextId,
                        principalTable: "media_contexts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_media_asset_generation_tasks_stories_StoryId",
                        column: x => x.StoryId,
                        principalTable: "stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_media_asset_generation_tasks_story_generation_jobs_BranchJo~",
                        column: x => x.BranchJobId,
                        principalTable: "story_generation_jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_media_asset_generation_tasks_story_versions_StoryVersionId",
                        column: x => x.StoryVersionId,
                        principalTable: "story_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_learning_artifact_generation_batches_RequestedByUserId",
                table: "learning_artifact_generation_batches",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_learning_artifact_generation_batches_RootJobId_Kind_BatchNu~",
                table: "learning_artifact_generation_batches",
                columns: new[] { "RootJobId", "Kind", "BatchNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_learning_artifact_generation_batches_StoryId_Status_LeaseEx~",
                table: "learning_artifact_generation_batches",
                columns: new[] { "StoryId", "Status", "LeaseExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_learning_artifact_generation_batches_StoryVersionId",
                table: "learning_artifact_generation_batches",
                column: "StoryVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_media_asset_generation_tasks_BranchJobId_PackageRevision_St~",
                table: "media_asset_generation_tasks",
                columns: new[] { "BranchJobId", "PackageRevision", "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_media_asset_generation_tasks_IllustrationBeatId",
                table: "media_asset_generation_tasks",
                column: "IllustrationBeatId");

            migrationBuilder.CreateIndex(
                name: "IX_media_asset_generation_tasks_MediaAssetId",
                table: "media_asset_generation_tasks",
                column: "MediaAssetId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_media_asset_generation_tasks_MediaContextId",
                table: "media_asset_generation_tasks",
                column: "MediaContextId");

            migrationBuilder.CreateIndex(
                name: "IX_media_asset_generation_tasks_Status_NextAttemptAt_QueuedAt",
                table: "media_asset_generation_tasks",
                columns: new[] { "Status", "NextAttemptAt", "QueuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_media_asset_generation_tasks_StoryId_Status_LeaseExpiresAt",
                table: "media_asset_generation_tasks",
                columns: new[] { "StoryId", "Status", "LeaseExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_media_asset_generation_tasks_StoryVersionId",
                table: "media_asset_generation_tasks",
                column: "StoryVersionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "learning_artifact_generation_batches");

            migrationBuilder.DropTable(
                name: "media_asset_generation_tasks");

            migrationBuilder.DropColumn(
                name: "Example",
                table: "story_vocabulary");

            migrationBuilder.DropColumn(
                name: "SourceEnd",
                table: "story_vocabulary");

            migrationBuilder.DropColumn(
                name: "SourceQuote",
                table: "story_vocabulary");

            migrationBuilder.DropColumn(
                name: "SourceStart",
                table: "story_vocabulary");

            migrationBuilder.DropColumn(
                name: "Explanation",
                table: "quiz_items");

            migrationBuilder.DropColumn(
                name: "Hint",
                table: "quiz_items");

            migrationBuilder.DropColumn(
                name: "LearningGoal",
                table: "quiz_items");

            migrationBuilder.DropColumn(
                name: "OptionFeedbackJson",
                table: "quiz_items");

            migrationBuilder.DropColumn(
                name: "SourceEnd",
                table: "quiz_items");

            migrationBuilder.DropColumn(
                name: "SourceQuote",
                table: "quiz_items");

            migrationBuilder.DropColumn(
                name: "SourceStart",
                table: "quiz_items");
        }
    }
}
