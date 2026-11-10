using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCreditAdjustmentsAndReconciliationExceptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AiCredits",
                table: "payment_transactions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "credit_adjustments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    AdminId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credit_adjustments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_credit_adjustments_user_accounts_AdminId",
                        column: x => x.AdminId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_credit_adjustments_user_accounts_UserId",
                        column: x => x.UserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_reconciliation_exceptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PaymentId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ResolvedBy = table.Column<int>(type: "integer", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_reconciliation_exceptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_payment_reconciliation_exceptions_payment_transactions_Paym~",
                        column: x => x.PaymentId,
                        principalTable: "payment_transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payment_reconciliation_exceptions_user_accounts_ResolvedBy",
                        column: x => x.ResolvedBy,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_credit_adjustments_AdminId",
                table: "credit_adjustments",
                column: "AdminId");

            migrationBuilder.CreateIndex(
                name: "IX_credit_adjustments_UserId",
                table: "credit_adjustments",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_payment_reconciliation_exceptions_PaymentId",
                table: "payment_reconciliation_exceptions",
                column: "PaymentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_reconciliation_exceptions_ResolvedBy",
                table: "payment_reconciliation_exceptions",
                column: "ResolvedBy");

            migrationBuilder.CreateIndex(
                name: "IX_payment_reconciliation_exceptions_Status",
                table: "payment_reconciliation_exceptions",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "credit_adjustments");

            migrationBuilder.DropTable(
                name: "payment_reconciliation_exceptions");

            migrationBuilder.DropColumn(
                name: "AiCredits",
                table: "payment_transactions");
        }
    }
}
