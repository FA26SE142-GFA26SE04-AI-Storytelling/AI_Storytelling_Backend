using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddModeratorFlowSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CommitmentAcceptedAt",
                table: "user_accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Expertise",
                table: "user_accounts",
                type: "character varying(200)",
                maxLength: 200,
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
                name: "ModerationZone",
                table: "story_versions",
                type: "character varying(30)",
                maxLength: 30,
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
                name: "Source",
                table: "story_categories",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Ai");

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "stories",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EverGrayZone",
                table: "stories",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PublishedAt",
                table: "stories",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentUrl",
                table: "content_reports",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsReviewItem",
                table: "content_reports",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ReadingRoadmapId",
                table: "content_reports",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResolvedAt",
                table: "content_reports",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StoryVersionId",
                table: "content_reports",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "reading_roadmaps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    AgeBand = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    MainCategoryId = table.Column<int>(type: "integer", nullable: false),
                    ReadingLevelMin = table.Column<int>(type: "integer", nullable: false),
                    ReadingLevelMax = table.Column<int>(type: "integer", nullable: false),
                    DefaultDaysPerStory = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "Draft"),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PublishedByUserId = table.Column<int>(type: "integer", nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClonedFromId = table.Column<int>(type: "integer", nullable: true),
                    ArchivedReason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ArchivedByUserId = table.Column<int>(type: "integer", nullable: true),
                    ArchiveNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ArchivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reading_roadmaps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_reading_roadmaps_content_categories_MainCategoryId",
                        column: x => x.MainCategoryId,
                        principalTable: "content_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reading_roadmaps_reading_roadmaps_ClonedFromId",
                        column: x => x.ClonedFromId,
                        principalTable: "reading_roadmaps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reading_roadmaps_user_accounts_ArchivedByUserId",
                        column: x => x.ArchivedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reading_roadmaps_user_accounts_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reading_roadmaps_user_accounts_PublishedByUserId",
                        column: x => x.PublishedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_items",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ItemType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "Pending"),
                    Priority = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "Medium"),
                    StoryId = table.Column<int>(type: "integer", nullable: true),
                    StoryVersionId = table.Column<int>(type: "integer", nullable: true),
                    ContentReportId = table.Column<int>(type: "integer", nullable: true),
                    ReadingRoadmapId = table.Column<int>(type: "integer", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    SourceChildProfileId = table.Column<int>(type: "integer", nullable: true),
                    CopyrightDeclaration = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    PiiCheckResult = table.Column<string>(type: "jsonb", nullable: true),
                    IsResubmission = table.Column<bool>(type: "boolean", nullable: false),
                    PreviousItemId = table.Column<int>(type: "integer", nullable: true),
                    ClaimedByUserId = table.Column<int>(type: "integer", nullable: true),
                    ClaimedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SlaDueAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SlaWarnedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Flags = table.Column<string>(type: "jsonb", nullable: true),
                    Checklist = table.Column<string>(type: "jsonb", nullable: true),
                    ReviewProgress = table.Column<string>(type: "jsonb", nullable: true),
                    Feedback = table.Column<string>(type: "jsonb", nullable: true),
                    DecisionReason = table.Column<string>(type: "text", nullable: true),
                    EscalationReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DecidedByUserId = table.Column<int>(type: "integer", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EscalatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedReason = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    WithdrawnAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RemovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RowVersion = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_moderation_items", x => x.Id);
                    table.CheckConstraint("ck_moderation_items_claimer_is_not_creator", "\"ClaimedByUserId\" IS NULL OR \"CreatedByUserId\" IS NULL OR \"ClaimedByUserId\" <> \"CreatedByUserId\"");
                    table.ForeignKey(
                        name: "FK_moderation_items_child_profiles_SourceChildProfileId",
                        column: x => x.SourceChildProfileId,
                        principalTable: "child_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_moderation_items_content_reports_ContentReportId",
                        column: x => x.ContentReportId,
                        principalTable: "content_reports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_moderation_items_moderation_items_PreviousItemId",
                        column: x => x.PreviousItemId,
                        principalTable: "moderation_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_moderation_items_reading_roadmaps_ReadingRoadmapId",
                        column: x => x.ReadingRoadmapId,
                        principalTable: "reading_roadmaps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_moderation_items_stories_StoryId",
                        column: x => x.StoryId,
                        principalTable: "stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_moderation_items_story_versions_StoryVersionId",
                        column: x => x.StoryVersionId,
                        principalTable: "story_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_moderation_items_user_accounts_ClaimedByUserId",
                        column: x => x.ClaimedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_moderation_items_user_accounts_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_moderation_items_user_accounts_DecidedByUserId",
                        column: x => x.DecidedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_review_views",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ModeratorUserId = table.Column<int>(type: "integer", nullable: false),
                    StoryId = table.Column<int>(type: "integer", nullable: true),
                    StoryVersionId = table.Column<int>(type: "integer", nullable: true),
                    ReadingRoadmapId = table.Column<int>(type: "integer", nullable: true),
                    SourceTab = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    GrantedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_moderation_review_views", x => x.Id);
                    table.CheckConstraint("ck_moderation_review_views_has_target", "\"StoryId\" IS NOT NULL OR \"ReadingRoadmapId\" IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_moderation_review_views_reading_roadmaps_ReadingRoadmapId",
                        column: x => x.ReadingRoadmapId,
                        principalTable: "reading_roadmaps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_moderation_review_views_stories_StoryId",
                        column: x => x.StoryId,
                        principalTable: "stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_moderation_review_views_story_versions_StoryVersionId",
                        column: x => x.StoryVersionId,
                        principalTable: "story_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_moderation_review_views_user_accounts_ModeratorUserId",
                        column: x => x.ModeratorUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reading_roadmap_items",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReadingRoadmapId = table.Column<int>(type: "integer", nullable: false),
                    StoryId = table.Column<int>(type: "integer", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Days = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reading_roadmap_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_reading_roadmap_items_reading_roadmaps_ReadingRoadmapId",
                        column: x => x.ReadingRoadmapId,
                        principalTable: "reading_roadmaps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_reading_roadmap_items_stories_StoryId",
                        column: x => x.StoryId,
                        principalTable: "stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_story_versions_GrayZoneClearedByUserId",
                table: "story_versions",
                column: "GrayZoneClearedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_story_versions_ReviewedByUserId",
                table: "story_versions",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_content_reports_ReadingRoadmapId",
                table: "content_reports",
                column: "ReadingRoadmapId");

            migrationBuilder.CreateIndex(
                name: "IX_content_reports_StoryVersionId",
                table: "content_reports",
                column: "StoryVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_moderation_items_ClaimedByUserId_Status",
                table: "moderation_items",
                columns: new[] { "ClaimedByUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_moderation_items_ContentReportId",
                table: "moderation_items",
                column: "ContentReportId");

            migrationBuilder.CreateIndex(
                name: "IX_moderation_items_CreatedByUserId",
                table: "moderation_items",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_moderation_items_DecidedByUserId",
                table: "moderation_items",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_moderation_items_ItemType_Status_Priority",
                table: "moderation_items",
                columns: new[] { "ItemType", "Status", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_moderation_items_PreviousItemId",
                table: "moderation_items",
                column: "PreviousItemId");

            migrationBuilder.CreateIndex(
                name: "IX_moderation_items_ReadingRoadmapId",
                table: "moderation_items",
                column: "ReadingRoadmapId",
                unique: true,
                filter: "\"ItemType\" = 'RoadmapTemplateReview' AND \"Status\" IN ('Pending', 'Claimed') AND NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_moderation_items_SourceChildProfileId",
                table: "moderation_items",
                column: "SourceChildProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_moderation_items_StoryId",
                table: "moderation_items",
                column: "StoryId",
                unique: true,
                filter: "\"ItemType\" = 'CommunitySubmission' AND \"Status\" IN ('Pending', 'Approved') AND NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_moderation_items_StoryVersionId",
                table: "moderation_items",
                column: "StoryVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_moderation_review_views_ModeratorUserId_ReadingRoadmapId_Ex~",
                table: "moderation_review_views",
                columns: new[] { "ModeratorUserId", "ReadingRoadmapId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_moderation_review_views_ModeratorUserId_StoryId_ExpiresAt",
                table: "moderation_review_views",
                columns: new[] { "ModeratorUserId", "StoryId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_moderation_review_views_ReadingRoadmapId",
                table: "moderation_review_views",
                column: "ReadingRoadmapId");

            migrationBuilder.CreateIndex(
                name: "IX_moderation_review_views_StoryId",
                table: "moderation_review_views",
                column: "StoryId");

            migrationBuilder.CreateIndex(
                name: "IX_moderation_review_views_StoryVersionId",
                table: "moderation_review_views",
                column: "StoryVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_reading_roadmap_items_ReadingRoadmapId_Position",
                table: "reading_roadmap_items",
                columns: new[] { "ReadingRoadmapId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reading_roadmap_items_ReadingRoadmapId_StoryId",
                table: "reading_roadmap_items",
                columns: new[] { "ReadingRoadmapId", "StoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reading_roadmap_items_StoryId",
                table: "reading_roadmap_items",
                column: "StoryId");

            migrationBuilder.CreateIndex(
                name: "IX_reading_roadmaps_AgeBand_MainCategoryId",
                table: "reading_roadmaps",
                columns: new[] { "AgeBand", "MainCategoryId" },
                unique: true,
                filter: "\"Status\" = 'Published' AND NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_reading_roadmaps_ArchivedByUserId",
                table: "reading_roadmaps",
                column: "ArchivedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_reading_roadmaps_ClonedFromId",
                table: "reading_roadmaps",
                column: "ClonedFromId",
                unique: true,
                filter: "\"Status\" IN ('Draft', 'PendingReview') AND NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_reading_roadmaps_CreatedByUserId",
                table: "reading_roadmaps",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_reading_roadmaps_MainCategoryId",
                table: "reading_roadmaps",
                column: "MainCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_reading_roadmaps_PublishedByUserId",
                table: "reading_roadmaps",
                column: "PublishedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_content_reports_reading_roadmaps_ReadingRoadmapId",
                table: "content_reports",
                column: "ReadingRoadmapId",
                principalTable: "reading_roadmaps",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_content_reports_story_versions_StoryVersionId",
                table: "content_reports",
                column: "StoryVersionId",
                principalTable: "story_versions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_content_reports_reading_roadmaps_ReadingRoadmapId",
                table: "content_reports");

            migrationBuilder.DropForeignKey(
                name: "FK_content_reports_story_versions_StoryVersionId",
                table: "content_reports");

            migrationBuilder.DropForeignKey(
                name: "FK_story_versions_user_accounts_GrayZoneClearedByUserId",
                table: "story_versions");

            migrationBuilder.DropForeignKey(
                name: "FK_story_versions_user_accounts_ReviewedByUserId",
                table: "story_versions");

            migrationBuilder.DropTable(
                name: "moderation_items");

            migrationBuilder.DropTable(
                name: "moderation_review_views");

            migrationBuilder.DropTable(
                name: "reading_roadmap_items");

            migrationBuilder.DropTable(
                name: "reading_roadmaps");

            migrationBuilder.DropIndex(
                name: "IX_story_versions_GrayZoneClearedByUserId",
                table: "story_versions");

            migrationBuilder.DropIndex(
                name: "IX_story_versions_ReviewedByUserId",
                table: "story_versions");

            migrationBuilder.DropIndex(
                name: "IX_content_reports_ReadingRoadmapId",
                table: "content_reports");

            migrationBuilder.DropIndex(
                name: "IX_content_reports_StoryVersionId",
                table: "content_reports");

            migrationBuilder.DropColumn(
                name: "CommitmentAcceptedAt",
                table: "user_accounts");

            migrationBuilder.DropColumn(
                name: "Expertise",
                table: "user_accounts");

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
                name: "ModerationZone",
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
                name: "Source",
                table: "story_categories");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "stories");

            migrationBuilder.DropColumn(
                name: "EverGrayZone",
                table: "stories");

            migrationBuilder.DropColumn(
                name: "PublishedAt",
                table: "stories");

            migrationBuilder.DropColumn(
                name: "AttachmentUrl",
                table: "content_reports");

            migrationBuilder.DropColumn(
                name: "IsReviewItem",
                table: "content_reports");

            migrationBuilder.DropColumn(
                name: "ReadingRoadmapId",
                table: "content_reports");

            migrationBuilder.DropColumn(
                name: "ResolvedAt",
                table: "content_reports");

            migrationBuilder.DropColumn(
                name: "StoryVersionId",
                table: "content_reports");

        }
    }
}
