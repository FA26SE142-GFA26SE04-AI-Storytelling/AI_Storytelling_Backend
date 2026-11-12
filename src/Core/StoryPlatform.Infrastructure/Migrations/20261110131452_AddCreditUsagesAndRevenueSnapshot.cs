using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCreditUsagesAndRevenueSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RevenueSnapshotJson",
                table: "business_reports",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "credit_usages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    LotId = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credit_usages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_credit_usages_credit_lots_LotId",
                        column: x => x.LotId,
                        principalTable: "credit_lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_credit_usages_user_accounts_UserId",
                        column: x => x.UserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_credit_usages_CreatedAt",
                table: "credit_usages",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_credit_usages_LotId",
                table: "credit_usages",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_credit_usages_UserId",
                table: "credit_usages",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "credit_usages");

            migrationBuilder.DropColumn(
                name: "RevenueSnapshotJson",
                table: "business_reports");
        }
    }
}
