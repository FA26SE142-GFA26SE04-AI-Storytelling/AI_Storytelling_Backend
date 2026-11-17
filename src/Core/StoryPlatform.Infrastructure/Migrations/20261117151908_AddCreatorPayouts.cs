using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCreatorPayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "creator_earning_periods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PeriodMonth = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatorFundVnd = table.Column<long>(type: "bigint", nullable: false),
                    TotalValidReads = table.Column<int>(type: "integer", nullable: false),
                    DistributedVnd = table.Column<long>(type: "bigint", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_creator_earning_periods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "creator_payout_accounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CreatorUserId = table.Column<int>(type: "integer", nullable: false),
                    BankName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AccountNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AccountHolderName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_creator_payout_accounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_creator_payout_accounts_user_accounts_CreatorUserId",
                        column: x => x.CreatorUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "creator_balance_entries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CreatorUserId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AmountVnd = table.Column<long>(type: "bigint", nullable: false),
                    EarningPeriodId = table.Column<int>(type: "integer", nullable: true),
                    ReversedEntryId = table.Column<int>(type: "integer", nullable: true),
                    ValidReads = table.Column<int>(type: "integer", nullable: true),
                    BankTransactionCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TransferredOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BankName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AccountNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    AccountHolderName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RecordedByUserId = table.Column<int>(type: "integer", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_creator_balance_entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_creator_balance_entries_creator_balance_entries_ReversedEnt~",
                        column: x => x.ReversedEntryId,
                        principalTable: "creator_balance_entries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_creator_balance_entries_creator_earning_periods_EarningPeri~",
                        column: x => x.EarningPeriodId,
                        principalTable: "creator_earning_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_creator_balance_entries_user_accounts_CreatorUserId",
                        column: x => x.CreatorUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_creator_balance_entries_CreatorUserId",
                table: "creator_balance_entries",
                column: "CreatorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_creator_balance_entries_EarningPeriodId",
                table: "creator_balance_entries",
                column: "EarningPeriodId");

            migrationBuilder.CreateIndex(
                name: "UX_creator_balance_entries_period_per_creator",
                table: "creator_balance_entries",
                columns: new[] { "CreatorUserId", "EarningPeriodId" },
                unique: true,
                filter: "\"EarningPeriodId\" IS NOT NULL AND NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "UX_creator_balance_entries_reversal",
                table: "creator_balance_entries",
                column: "ReversedEntryId",
                unique: true,
                filter: "\"ReversedEntryId\" IS NOT NULL AND NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_creator_earning_periods_PeriodMonth",
                table: "creator_earning_periods",
                column: "PeriodMonth",
                unique: true,
                filter: "NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_creator_payout_accounts_CreatorUserId",
                table: "creator_payout_accounts",
                column: "CreatorUserId",
                unique: true,
                filter: "NOT \"IsDeleted\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "creator_balance_entries");

            migrationBuilder.DropTable(
                name: "creator_payout_accounts");

            migrationBuilder.DropTable(
                name: "creator_earning_periods");
        }
    }
}
