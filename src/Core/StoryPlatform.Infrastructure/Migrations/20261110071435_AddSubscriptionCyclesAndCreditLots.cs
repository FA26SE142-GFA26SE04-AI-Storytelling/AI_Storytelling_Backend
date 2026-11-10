using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionCyclesAndCreditLots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "QuotaAmount",
                table: "subscription_plans",
                newName: "AiCredits");

            migrationBuilder.AddColumn<string>(
                name: "PlanType",
                table: "subscription_plans",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Legacy");

            migrationBuilder.CreateTable(
                name: "credit_lots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    CreditType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Total = table.Column<int>(type: "integer", nullable: false),
                    Remaining = table.Column<int>(type: "integer", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PaymentId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credit_lots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_credit_lots_payment_transactions_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "payment_transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_credit_lots_user_accounts_UserId",
                        column: x => x.UserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_subscriptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    PlanId = table.Column<int>(type: "integer", nullable: false),
                    StartAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PaymentId = table.Column<int>(type: "integer", nullable: false),
                    RenewalReminderSentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_subscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_subscriptions_payment_transactions_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "payment_transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_subscriptions_subscription_plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "subscription_plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_subscriptions_user_accounts_UserId",
                        column: x => x.UserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "subscription_plans",
                keyColumn: "Id",
                keyValue: 1,
                column: "IsActive",
                value: false);

            migrationBuilder.InsertData(
                table: "subscription_plans",
                columns: new[] { "Id", "AiCredits", "CreatedAt", "IsActive", "IsDeleted", "Name", "PlanType", "PriceVnd", "UpdatedAt" },
                values: new object[,]
                {
                    { 2, 0, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, false, "Gói Đọc", "Reading", 49000, null },
                    { 3, 4, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, false, "Gói Sáng tạo", "Creative", 99000, null },
                    { 4, 2, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, false, "Add-on", "AddOn", 29000, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_credit_lots_PaymentId",
                table: "credit_lots",
                column: "PaymentId",
                unique: true,
                filter: "\"PaymentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_credit_lots_UserId_CreditType",
                table: "credit_lots",
                columns: new[] { "UserId", "CreditType" });

            migrationBuilder.CreateIndex(
                name: "IX_credit_lots_UserId_Trial",
                table: "credit_lots",
                column: "UserId",
                unique: true,
                filter: "\"Source\" = 'Trial' AND NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_user_subscriptions_PaymentId",
                table: "user_subscriptions",
                column: "PaymentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_subscriptions_PlanId",
                table: "user_subscriptions",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_user_subscriptions_UserId_Status",
                table: "user_subscriptions",
                columns: new[] { "UserId", "Status" });

            // Goi moi duoc chen voi Id co dinh; day sequence len de goi tao sau nay khong trung khoa.
            migrationBuilder.Sql(
                """
                SELECT setval(pg_get_serial_sequence('subscription_plans', 'Id'),
                              (SELECT MAX("Id") FROM subscription_plans));
                """);

            // Luot con du cua mo hinh cu (token_quota_configs scope Personal) thanh mot lo Legacy khong het han.
            // Bang token_quota_configs duoc giu nguyen, chi khong con duoc doc.
            migrationBuilder.Sql(
                """
                INSERT INTO credit_lots ("UserId", "CreditType", "Source", "Total", "Remaining", "ExpiresAt", "PaymentId", "CreatedAt", "IsDeleted")
                SELECT "UserId", 'AI', 'Legacy', "QuotaLimit" - "QuotaUsed", "QuotaLimit" - "QuotaUsed", NULL, NULL, NOW(), FALSE
                FROM token_quota_configs
                WHERE "Scope" = 'Personal' AND "IsDeleted" = FALSE AND "UserId" IS NOT NULL AND "QuotaLimit" - "QuotaUsed" > 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "credit_lots");

            migrationBuilder.DropTable(
                name: "user_subscriptions");

            migrationBuilder.DeleteData(
                table: "subscription_plans",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "subscription_plans",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "subscription_plans",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DropColumn(
                name: "PlanType",
                table: "subscription_plans");

            migrationBuilder.RenameColumn(
                name: "AiCredits",
                table: "subscription_plans",
                newName: "QuotaAmount");

            migrationBuilder.UpdateData(
                table: "subscription_plans",
                keyColumn: "Id",
                keyValue: 1,
                column: "IsActive",
                value: true);
        }
    }
}
