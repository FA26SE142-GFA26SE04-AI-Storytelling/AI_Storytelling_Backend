using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropRecommendationHoldModeAndIntervention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_child_profile_version_history_recommendations_Recommendatio~",
                table: "child_profile_version_history");

            migrationBuilder.DropTable(
                name: "intervention_cases");

            migrationBuilder.DropTable(
                name: "recommendation_reviews");

            migrationBuilder.DropTable(
                name: "recommendations");

            migrationBuilder.DropTable(
                name: "learning_insights");

            migrationBuilder.DropIndex(
                name: "IX_child_profile_version_history_RecommendationId",
                table: "child_profile_version_history");

            migrationBuilder.DropColumn(
                name: "RecommendationId",
                table: "child_profile_version_history");

            migrationBuilder.AlterColumn<string>(
                name: "Source",
                table: "child_profile_version_history",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Manual",
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldDefaultValue: "Recommendation");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Source",
                table: "child_profile_version_history",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Recommendation",
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldDefaultValue: "Manual");

            migrationBuilder.AddColumn<int>(
                name: "RecommendationId",
                table: "child_profile_version_history",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "learning_insights",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Evidence = table.Column<string>(type: "text", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Observation = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_learning_insights", x => x.Id);
                    table.ForeignKey(
                        name: "FK_learning_insights_child_profiles_ChildProfileId",
                        column: x => x.ChildProfileId,
                        principalTable: "child_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recommendations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: false),
                    LearningInsightId = table.Column<int>(type: "integer", nullable: false),
                    Category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CurrentState = table.Column<string>(type: "text", nullable: false),
                    Evidence = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    ProposedChange = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recommendations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_recommendations_child_profiles_ChildProfileId",
                        column: x => x.ChildProfileId,
                        principalTable: "child_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recommendations_learning_insights_LearningInsightId",
                        column: x => x.LearningInsightId,
                        principalTable: "learning_insights",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "intervention_cases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: false),
                    NotesAddedByUserId = table.Column<int>(type: "integer", nullable: true),
                    RecommendationId = table.Column<int>(type: "integer", nullable: true),
                    ResolvedByUserId = table.Column<int>(type: "integer", nullable: true),
                    TriggeringStoryId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    NotesAddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OpenedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolutionType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SkillGapNotes = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    TriggerType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_intervention_cases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_intervention_cases_child_profiles_ChildProfileId",
                        column: x => x.ChildProfileId,
                        principalTable: "child_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_intervention_cases_recommendations_RecommendationId",
                        column: x => x.RecommendationId,
                        principalTable: "recommendations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_intervention_cases_stories_TriggeringStoryId",
                        column: x => x.TriggeringStoryId,
                        principalTable: "stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_intervention_cases_user_accounts_NotesAddedByUserId",
                        column: x => x.NotesAddedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_intervention_cases_user_accounts_ResolvedByUserId",
                        column: x => x.ResolvedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recommendation_reviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RecommendationId = table.Column<int>(type: "integer", nullable: false),
                    ReviewerUserId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Decision = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    HadFinalAuthority = table.Column<bool>(type: "boolean", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    IsFinal = table.Column<bool>(type: "boolean", nullable: false),
                    ModifiedValue = table.Column<string>(type: "text", nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recommendation_reviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_recommendation_reviews_recommendations_RecommendationId",
                        column: x => x.RecommendationId,
                        principalTable: "recommendations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recommendation_reviews_user_accounts_ReviewerUserId",
                        column: x => x.ReviewerUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_child_profile_version_history_RecommendationId",
                table: "child_profile_version_history",
                column: "RecommendationId");

            migrationBuilder.CreateIndex(
                name: "IX_intervention_cases_ChildProfileId",
                table: "intervention_cases",
                column: "ChildProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_intervention_cases_NotesAddedByUserId",
                table: "intervention_cases",
                column: "NotesAddedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_intervention_cases_RecommendationId",
                table: "intervention_cases",
                column: "RecommendationId");

            migrationBuilder.CreateIndex(
                name: "IX_intervention_cases_ResolvedByUserId",
                table: "intervention_cases",
                column: "ResolvedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_intervention_cases_TriggeringStoryId",
                table: "intervention_cases",
                column: "TriggeringStoryId");

            migrationBuilder.CreateIndex(
                name: "IX_learning_insights_ChildProfileId",
                table: "learning_insights",
                column: "ChildProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_recommendation_reviews_RecommendationId",
                table: "recommendation_reviews",
                column: "RecommendationId");

            migrationBuilder.CreateIndex(
                name: "IX_recommendation_reviews_ReviewerUserId",
                table: "recommendation_reviews",
                column: "ReviewerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_recommendations_ChildProfileId",
                table: "recommendations",
                column: "ChildProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_recommendations_LearningInsightId",
                table: "recommendations",
                column: "LearningInsightId");

            migrationBuilder.AddForeignKey(
                name: "FK_child_profile_version_history_recommendations_Recommendatio~",
                table: "child_profile_version_history",
                column: "RecommendationId",
                principalTable: "recommendations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
