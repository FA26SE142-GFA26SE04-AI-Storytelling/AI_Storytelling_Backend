using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEventAndRewardCampaigns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EventId",
                table: "credit_lots",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "events",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "character varying(220)", maxLength: 220, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    BannerUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    MainCategoryId = table.Column<int>(type: "integer", nullable: false),
                    TargetAgeBands = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BudgetVnd = table.Column<long>(type: "bigint", nullable: false),
                    AppealDeadlineAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: false),
                    AssignedModeratorId = table.Column<int>(type: "integer", nullable: false),
                    ApprovedByAdminId = table.Column<int>(type: "integer", nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SummaryReportJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_events_content_categories_MainCategoryId",
                        column: x => x.MainCategoryId,
                        principalTable: "content_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "event_awards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventId = table.Column<int>(type: "integer", nullable: false),
                    TrackId = table.Column<int>(type: "integer", nullable: false),
                    RankOrTier = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SlotIndex = table.Column<int>(type: "integer", nullable: false),
                    RewardType = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    RewardName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UnitValueVnd = table.Column<long>(type: "bigint", nullable: false),
                    CreditGrantAmount = table.Column<int>(type: "integer", nullable: false),
                    ParticipationId = table.Column<int>(type: "integer", nullable: true),
                    ReallocatedFromAwardId = table.Column<int>(type: "integer", nullable: true),
                    ReallocationRound = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ClaimDeadlineAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovedByAdminId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_awards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_event_awards_events_EventId",
                        column: x => x.EventId,
                        principalTable: "events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "event_rule_versions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventId = table.Column<int>(type: "integer", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RulesText = table.Column<string>(type: "text", nullable: false),
                    CreatorRubricJson = table.Column<string>(type: "text", nullable: true),
                    CompletionMode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RequiredTaskCount = table.Column<int>(type: "integer", nullable: true),
                    RequiredPoints = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ActivatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_rule_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_event_rule_versions_events_EventId",
                        column: x => x.EventId,
                        principalTable: "events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "event_stories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventId = table.Column<int>(type: "integer", nullable: false),
                    StoryId = table.Column<int>(type: "integer", nullable: false),
                    StoryVersionId = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EventSubmissionId = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_stories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_event_stories_events_EventId",
                        column: x => x.EventId,
                        principalTable: "events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "event_tracks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventId = table.Column<int>(type: "integer", nullable: false),
                    TrackType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RegistrationStartAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RegistrationEndAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExecutionStartAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExecutionEndAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MaxSubmissionsPerUser = table.Column<int>(type: "integer", nullable: false),
                    GrantAiCredits = table.Column<int>(type: "integer", nullable: false),
                    GrantSubmitCredits = table.Column<int>(type: "integer", nullable: false),
                    WaiveWeeklyLibraryLimit = table.Column<bool>(type: "boolean", nullable: false),
                    ClaimWindowHours = table.Column<int>(type: "integer", nullable: false),
                    MaxReallocationRounds = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_tracks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_event_tracks_events_EventId",
                        column: x => x.EventId,
                        principalTable: "events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reward_fulfillments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventAwardId = table.Column<int>(type: "integer", nullable: false),
                    AttemptNo = table.Column<int>(type: "integer", nullable: false),
                    RecipientInfoProtectedJson = table.Column<string>(type: "text", nullable: true),
                    TrackingOrBankTxCode = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    HandledByUserId = table.Column<int>(type: "integer", nullable: true),
                    DeliveredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reward_fulfillments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_reward_fulfillments_event_awards_EventAwardId",
                        column: x => x.EventAwardId,
                        principalTable: "event_awards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "event_task_definitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventRuleVersionId = table.Column<int>(type: "integer", nullable: false),
                    TaskCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    VerifierType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    TargetValue = table.Column<int>(type: "integer", nullable: false),
                    ConfigJson = table.Column<string>(type: "text", nullable: true),
                    IsMandatory = table.Column<bool>(type: "boolean", nullable: false),
                    Points = table.Column<int>(type: "integer", nullable: false),
                    AllowMultiplePerStory = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_task_definitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_event_task_definitions_event_rule_versions_EventRuleVersion~",
                        column: x => x.EventRuleVersionId,
                        principalTable: "event_rule_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "event_participations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventId = table.Column<int>(type: "integer", nullable: false),
                    TrackId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    AcceptedRuleVersionId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MissionCompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VerificationNotesJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_participations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_event_participations_event_tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "event_tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_event_participations_events_EventId",
                        column: x => x.EventId,
                        principalTable: "events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "event_appeals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventId = table.Column<int>(type: "integer", nullable: false),
                    ParticipationId = table.Column<int>(type: "integer", nullable: false),
                    AppellantUserId = table.Column<int>(type: "integer", nullable: false),
                    ReasonText = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    EvidenceJson = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ReviewedByUserId = table.Column<int>(type: "integer", nullable: true),
                    ResolutionNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_appeals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_event_appeals_event_participations_ParticipationId",
                        column: x => x.ParticipationId,
                        principalTable: "event_participations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "event_submissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventId = table.Column<int>(type: "integer", nullable: false),
                    ParticipationId = table.Column<int>(type: "integer", nullable: false),
                    CreatorUserId = table.Column<int>(type: "integer", nullable: false),
                    StoryId = table.Column<int>(type: "integer", nullable: false),
                    CurrentStoryVersionId = table.Column<int>(type: "integer", nullable: false),
                    LockedStoryVersionId = table.Column<int>(type: "integer", nullable: true),
                    LatestModerationItemId = table.Column<int>(type: "integer", nullable: true),
                    SimilarityScore = table.Column<double>(type: "double precision", nullable: true),
                    SimilarityReferenceStoryId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_submissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_event_submissions_event_participations_ParticipationId",
                        column: x => x.ParticipationId,
                        principalTable: "event_participations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_event_submissions_events_EventId",
                        column: x => x.EventId,
                        principalTable: "events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "event_task_progress",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ParticipationId = table.Column<int>(type: "integer", nullable: false),
                    TaskDefinitionId = table.Column<int>(type: "integer", nullable: false),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: true),
                    StoryId = table.Column<int>(type: "integer", nullable: true),
                    ReadingSessionId = table.Column<int>(type: "integer", nullable: true),
                    StoryRatingId = table.Column<int>(type: "integer", nullable: true),
                    SelfDeclaredPayloadJson = table.Column<string>(type: "text", nullable: true),
                    ActivityLocalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_task_progress", x => x.Id);
                    table.ForeignKey(
                        name: "FK_event_task_progress_event_participations_ParticipationId",
                        column: x => x.ParticipationId,
                        principalTable: "event_participations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_event_task_progress_event_task_definitions_TaskDefinitionId",
                        column: x => x.TaskDefinitionId,
                        principalTable: "event_task_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "event_evaluations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventSubmissionId = table.Column<int>(type: "integer", nullable: false),
                    EvaluatorUserId = table.Column<int>(type: "integer", nullable: false),
                    CriteriaScoresJson = table.Column<string>(type: "text", nullable: false),
                    TotalWeightedScore = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_evaluations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_event_evaluations_event_submissions_EventSubmissionId",
                        column: x => x.EventSubmissionId,
                        principalTable: "event_submissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_credit_lots_EventId",
                table: "credit_lots",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_event_appeals_ActivePerParticipation",
                table: "event_appeals",
                column: "ParticipationId",
                unique: true,
                filter: "\"Status\" IN ('Open', 'UnderReview', 'Escalated') AND NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_event_awards_ActiveSlot",
                table: "event_awards",
                columns: new[] { "EventId", "TrackId", "RankOrTier", "SlotIndex" },
                unique: true,
                filter: "\"Status\" NOT IN ('Expired', 'UnclaimedBudgetReturned', 'Failed') AND NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_event_evaluations_EventSubmissionId_EvaluatorUserId",
                table: "event_evaluations",
                columns: new[] { "EventSubmissionId", "EvaluatorUserId" },
                unique: true,
                filter: "NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_event_participations_EventId_TrackId_UserId",
                table: "event_participations",
                columns: new[] { "EventId", "TrackId", "UserId" },
                unique: true,
                filter: "NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_event_participations_TrackId",
                table: "event_participations",
                column: "TrackId");

            migrationBuilder.CreateIndex(
                name: "IX_event_participations_UserId_Status",
                table: "event_participations",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_event_rule_versions_EventId_VersionNo",
                table: "event_rule_versions",
                columns: new[] { "EventId", "VersionNo" },
                unique: true,
                filter: "NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_event_stories_EventId_StoryId",
                table: "event_stories",
                columns: new[] { "EventId", "StoryId" },
                unique: true,
                filter: "NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_event_submissions_EventId_CreatorUserId",
                table: "event_submissions",
                columns: new[] { "EventId", "CreatorUserId" },
                unique: true,
                filter: "NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_event_submissions_EventId_StoryId",
                table: "event_submissions",
                columns: new[] { "EventId", "StoryId" },
                unique: true,
                filter: "NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_event_submissions_ParticipationId",
                table: "event_submissions",
                column: "ParticipationId");

            migrationBuilder.CreateIndex(
                name: "IX_event_task_definitions_EventRuleVersionId_TaskCode",
                table: "event_task_definitions",
                columns: new[] { "EventRuleVersionId", "TaskCode" },
                unique: true,
                filter: "NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_event_task_progress_ParticipationId_ActivityLocalDate",
                table: "event_task_progress",
                columns: new[] { "ParticipationId", "ActivityLocalDate" });

            migrationBuilder.CreateIndex(
                name: "IX_event_task_progress_ParticipationId_TaskDefinitionId",
                table: "event_task_progress",
                columns: new[] { "ParticipationId", "TaskDefinitionId" });

            migrationBuilder.CreateIndex(
                name: "IX_event_task_progress_TaskDefinitionId",
                table: "event_task_progress",
                column: "TaskDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_event_tracks_EventId_TrackType",
                table: "event_tracks",
                columns: new[] { "EventId", "TrackType" },
                unique: true,
                filter: "NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_events_MainCategoryId",
                table: "events",
                column: "MainCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_events_Slug",
                table: "events",
                column: "Slug",
                unique: true,
                filter: "NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_events_Status",
                table: "events",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_reward_fulfillments_EventAwardId_AttemptNo",
                table: "reward_fulfillments",
                columns: new[] { "EventAwardId", "AttemptNo" },
                unique: true,
                filter: "NOT \"IsDeleted\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "event_appeals");

            migrationBuilder.DropTable(
                name: "event_evaluations");

            migrationBuilder.DropTable(
                name: "event_stories");

            migrationBuilder.DropTable(
                name: "event_task_progress");

            migrationBuilder.DropTable(
                name: "reward_fulfillments");

            migrationBuilder.DropTable(
                name: "event_submissions");

            migrationBuilder.DropTable(
                name: "event_task_definitions");

            migrationBuilder.DropTable(
                name: "event_awards");

            migrationBuilder.DropTable(
                name: "event_participations");

            migrationBuilder.DropTable(
                name: "event_rule_versions");

            migrationBuilder.DropTable(
                name: "event_tracks");

            migrationBuilder.DropTable(
                name: "events");

            migrationBuilder.DropIndex(
                name: "IX_credit_lots_EventId",
                table: "credit_lots");

            migrationBuilder.DropColumn(
                name: "EventId",
                table: "credit_lots");
        }
    }
}
