using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanBenefitsAndSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CanCopyRoadmapTemplates",
                table: "subscription_plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MaxChildProfiles",
                table: "subscription_plans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReadAloudPerMonth",
                table: "subscription_plans",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WeeklyNewLibraryStories",
                table: "subscription_plans",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WritingAssistPerDay",
                table: "subscription_plans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReadingSlotSelectedAt",
                table: "child_profiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "plan_settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReadingFundPercent = table.Column<int>(type: "integer", nullable: false),
                    CreativeFundPercent = table.Column<int>(type: "integer", nullable: false),
                    CreatorMonthlyAiCredits = table.Column<int>(type: "integer", nullable: false),
                    CreatorMonthlySubmitCredits = table.Column<int>(type: "integer", nullable: false),
                    PartnerMonthlyAiCredits = table.Column<int>(type: "integer", nullable: false),
                    PartnerMonthlySubmitCredits = table.Column<int>(type: "integer", nullable: false),
                    MinimumReadSeconds = table.Column<int>(type: "integer", nullable: false),
                    TrialAiCredits = table.Column<int>(type: "integer", nullable: false),
                    FreeMaxChildProfiles = table.Column<int>(type: "integer", nullable: false),
                    FreeWeeklyNewLibraryStories = table.Column<int>(type: "integer", nullable: true),
                    FreeCanCopyRoadmapTemplates = table.Column<bool>(type: "boolean", nullable: false),
                    FreeReadAloudPerMonth = table.Column<int>(type: "integer", nullable: true),
                    FreeWritingAssistPerDay = table.Column<int>(type: "integer", nullable: false),
                    CreatorWritingAssistPerDay = table.Column<int>(type: "integer", nullable: false),
                    PartnerWritingAssistPerDay = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plan_settings", x => x.Id);
                    table.CheckConstraint("CK_plan_settings_fund_percent", "\"ReadingFundPercent\" BETWEEN 0 AND 100 AND \"CreativeFundPercent\" BETWEEN 0 AND 100");
                    table.CheckConstraint("CK_plan_settings_min_read_seconds", "\"MinimumReadSeconds\" BETWEEN 1 AND 3600");
                });

            migrationBuilder.InsertData(
                table: "plan_settings",
                columns: new[] { "Id", "CreatedAt", "CreativeFundPercent", "CreatorMonthlyAiCredits", "CreatorMonthlySubmitCredits", "CreatorWritingAssistPerDay", "FreeCanCopyRoadmapTemplates", "FreeMaxChildProfiles", "FreeReadAloudPerMonth", "FreeWeeklyNewLibraryStories", "FreeWritingAssistPerDay", "IsDeleted", "MinimumReadSeconds", "PartnerMonthlyAiCredits", "PartnerMonthlySubmitCredits", "PartnerWritingAssistPerDay", "ReadingFundPercent", "TrialAiCredits", "UpdatedAt" },
                values: new object[] { 1, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 10, 2, 5, 20, false, 1, 5, 3, 5, false, 60, 6, 10, 40, 30, 1, null });

            migrationBuilder.UpdateData(
                table: "subscription_plans",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CanCopyRoadmapTemplates", "MaxChildProfiles", "ReadAloudPerMonth", "WeeklyNewLibraryStories", "WritingAssistPerDay" },
                values: new object[] { false, 0, null, null, 0 });

            migrationBuilder.UpdateData(
                table: "subscription_plans",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "CanCopyRoadmapTemplates", "MaxChildProfiles", "ReadAloudPerMonth", "WeeklyNewLibraryStories", "WritingAssistPerDay" },
                values: new object[] { true, 4, 60, null, 5 });

            migrationBuilder.UpdateData(
                table: "subscription_plans",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "CanCopyRoadmapTemplates", "MaxChildProfiles", "ReadAloudPerMonth", "WeeklyNewLibraryStories", "WritingAssistPerDay" },
                values: new object[] { true, 4, 60, null, 20 });

            migrationBuilder.UpdateData(
                table: "subscription_plans",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "CanCopyRoadmapTemplates", "MaxChildProfiles", "ReadAloudPerMonth", "WeeklyNewLibraryStories", "WritingAssistPerDay" },
                values: new object[] { false, 0, null, null, 0 });

            migrationBuilder.UpdateData(
                table: "subscription_plans",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "CanCopyRoadmapTemplates", "MaxChildProfiles", "ReadAloudPerMonth", "WeeklyNewLibraryStories", "WritingAssistPerDay" },
                values: new object[] { false, 0, null, null, 0 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "plan_settings");

            migrationBuilder.DropColumn(
                name: "CanCopyRoadmapTemplates",
                table: "subscription_plans");

            migrationBuilder.DropColumn(
                name: "MaxChildProfiles",
                table: "subscription_plans");

            migrationBuilder.DropColumn(
                name: "ReadAloudPerMonth",
                table: "subscription_plans");

            migrationBuilder.DropColumn(
                name: "WeeklyNewLibraryStories",
                table: "subscription_plans");

            migrationBuilder.DropColumn(
                name: "WritingAssistPerDay",
                table: "subscription_plans");

            migrationBuilder.DropColumn(
                name: "ReadingSlotSelectedAt",
                table: "child_profiles");
        }
    }
}
