using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SoftDeleteLegacyPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "subscription_plans",
                keyColumn: "Id",
                keyValue: 1,
                column: "IsDeleted",
                value: true);

            // Xoa mem giao dich va doi soat cua goi Personal cu. credit_lots giu nguyen: luot da cap van dung duoc.
            migrationBuilder.Sql(
                """
                UPDATE payment_reconciliation_exceptions SET "IsDeleted" = TRUE, "UpdatedAt" = NOW()
                WHERE "PaymentId" IN (SELECT "Id" FROM payment_transactions WHERE "PlanId" = 1) AND "IsDeleted" = FALSE;

                UPDATE payment_transactions SET "IsDeleted" = TRUE, "UpdatedAt" = NOW()
                WHERE "PlanId" = 1 AND "IsDeleted" = FALSE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "subscription_plans",
                keyColumn: "Id",
                keyValue: 1,
                column: "IsDeleted",
                value: false);

            // Khoi phuc giao dich va doi soat cua goi Personal cu. credit_lots giu nguyen: luot da cap van dung duoc.
            migrationBuilder.Sql(
                """
                UPDATE payment_reconciliation_exceptions SET "IsDeleted" = FALSE, "UpdatedAt" = NOW()
                WHERE "PaymentId" IN (SELECT "Id" FROM payment_transactions WHERE "PlanId" = 1) AND "IsDeleted" = TRUE;

                UPDATE payment_transactions SET "IsDeleted" = FALSE, "UpdatedAt" = NOW()
                WHERE "PlanId" = 1 AND "IsDeleted" = TRUE;
                """);
        }
    }
}
