using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCreatorPartnerTier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreatorTier",
                table: "user_accounts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Standard");

            migrationBuilder.AddColumn<DateTime>(
                name: "PartnerSince",
                table: "user_accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "creator_tier_evaluations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CreatorUserId = table.Column<int>(type: "integer", nullable: false),
                    PeriodMonth = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    WindowStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    WindowEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PublishedStories = table.Column<int>(type: "integer", nullable: false),
                    CompletionRate = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    ValidReads = table.Column<int>(type: "integer", nullable: false),
                    ViolationRemovals = table.Column<int>(type: "integer", nullable: false),
                    Eligible = table.Column<bool>(type: "boolean", nullable: false),
                    TierBefore = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TierAfter = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_creator_tier_evaluations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_creator_tier_evaluations_user_accounts_CreatorUserId",
                        column: x => x.CreatorUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_creator_tier_evaluations_CreatorUserId_PeriodMonth",
                table: "creator_tier_evaluations",
                columns: new[] { "CreatorUserId", "PeriodMonth" },
                unique: true,
                filter: "NOT \"IsDeleted\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "creator_tier_evaluations");

            migrationBuilder.DropColumn(
                name: "CreatorTier",
                table: "user_accounts");

            migrationBuilder.DropColumn(
                name: "PartnerSince",
                table: "user_accounts");
        }
    }
}
