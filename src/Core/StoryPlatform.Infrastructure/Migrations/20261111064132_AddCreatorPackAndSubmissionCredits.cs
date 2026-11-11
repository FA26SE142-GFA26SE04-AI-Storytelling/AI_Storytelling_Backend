using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCreatorPackAndSubmissionCredits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_credit_lots_PaymentId",
                table: "credit_lots");

            migrationBuilder.AddColumn<string>(
                name: "Audience",
                table: "subscription_plans",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "User");

            migrationBuilder.AddColumn<int>(
                name: "SubmitCredits",
                table: "subscription_plans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SubmitCredits",
                table: "payment_transactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ModerationItemId",
                table: "credit_usages",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RefundedAt",
                table: "credit_usages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "subscription_plans",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Audience", "SubmitCredits" },
                values: new object[] { "User", 0 });

            migrationBuilder.UpdateData(
                table: "subscription_plans",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "Audience", "SubmitCredits" },
                values: new object[] { "User", 0 });

            migrationBuilder.UpdateData(
                table: "subscription_plans",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "Audience", "SubmitCredits" },
                values: new object[] { "User", 0 });

            migrationBuilder.UpdateData(
                table: "subscription_plans",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "Audience", "SubmitCredits" },
                values: new object[] { "User", 0 });

            migrationBuilder.InsertData(
                table: "subscription_plans",
                columns: new[] { "Id", "AiCredits", "Audience", "CreatedAt", "IsActive", "IsDeleted", "Name", "PlanType", "PriceVnd", "SubmitCredits", "UpdatedAt" },
                values: new object[] { 5, 6, "Creator", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, false, "Gói Creator", "CreatorPack", 99000, 20, null });

            migrationBuilder.CreateIndex(
                name: "IX_credit_usages_ModerationItemId",
                table: "credit_usages",
                column: "ModerationItemId");

            migrationBuilder.CreateIndex(
                name: "IX_credit_lots_CreatorMonthly",
                table: "credit_lots",
                columns: new[] { "UserId", "CreditType", "ExpiresAt" },
                unique: true,
                filter: "\"Source\" = 'CreatorMonthly' AND NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_credit_lots_PaymentId_CreditType",
                table: "credit_lots",
                columns: new[] { "PaymentId", "CreditType" },
                unique: true,
                filter: "\"PaymentId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_credit_usages_moderation_items_ModerationItemId",
                table: "credit_usages",
                column: "ModerationItemId",
                principalTable: "moderation_items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Goi Creator duoc chen voi Id co dinh; day sequence len de goi tao sau nay khong trung khoa.
            migrationBuilder.Sql(
                """
                SELECT setval(pg_get_serial_sequence('subscription_plans', 'Id'),
                              (SELECT MAX("Id") FROM subscription_plans));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_credit_usages_moderation_items_ModerationItemId",
                table: "credit_usages");

            migrationBuilder.DropIndex(
                name: "IX_credit_usages_ModerationItemId",
                table: "credit_usages");

            migrationBuilder.DropIndex(
                name: "IX_credit_lots_CreatorMonthly",
                table: "credit_lots");

            migrationBuilder.DropIndex(
                name: "IX_credit_lots_PaymentId_CreditType",
                table: "credit_lots");

            migrationBuilder.DeleteData(
                table: "subscription_plans",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DropColumn(
                name: "Audience",
                table: "subscription_plans");

            migrationBuilder.DropColumn(
                name: "SubmitCredits",
                table: "subscription_plans");

            migrationBuilder.DropColumn(
                name: "SubmitCredits",
                table: "payment_transactions");

            migrationBuilder.DropColumn(
                name: "ModerationItemId",
                table: "credit_usages");

            migrationBuilder.DropColumn(
                name: "RefundedAt",
                table: "credit_usages");

            migrationBuilder.CreateIndex(
                name: "IX_credit_lots_PaymentId",
                table: "credit_lots",
                column: "PaymentId",
                unique: true,
                filter: "\"PaymentId\" IS NOT NULL");
        }
    }
}
