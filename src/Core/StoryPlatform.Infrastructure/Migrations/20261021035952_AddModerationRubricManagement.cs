using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddModerationRubricManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "moderation_rubric_versions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    VersionNo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SchemaVersion = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    DefinitionJson = table.Column<string>(type: "jsonb", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: false),
                    ActivatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    ActivatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RetiredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_moderation_rubric_versions", x => x.Id);
                    table.CheckConstraint("CK_rubric_revision", "\"Revision\" > 0");
                    table.ForeignKey(
                        name: "FK_moderation_rubric_versions_user_accounts_ActivatedByUserId",
                        column: x => x.ActivatedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_moderation_rubric_versions_user_accounts_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "moderation_rubric_audit_logs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RubricVersionId = table.Column<int>(type: "integer", nullable: false),
                    ActorUserId = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    BeforeJson = table.Column<string>(type: "jsonb", nullable: true),
                    AfterJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_moderation_rubric_audit_logs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_moderation_rubric_audit_logs_moderation_rubric_versions_Rub~",
                        column: x => x.RubricVersionId,
                        principalTable: "moderation_rubric_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_moderation_rubric_audit_logs_user_accounts_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_moderation_rubric_audit_logs_ActorUserId",
                table: "moderation_rubric_audit_logs",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_moderation_rubric_audit_logs_RubricVersionId_CreatedAt",
                table: "moderation_rubric_audit_logs",
                columns: new[] { "RubricVersionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_moderation_rubric_versions_ActivatedByUserId",
                table: "moderation_rubric_versions",
                column: "ActivatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_moderation_rubric_versions_CreatedByUserId",
                table: "moderation_rubric_versions",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_moderation_rubric_active",
                table: "moderation_rubric_versions",
                column: "Code",
                unique: true,
                filter: "\"Status\" = 'Active' AND NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "UX_moderation_rubric_version",
                table: "moderation_rubric_versions",
                columns: new[] { "Code", "VersionNo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "moderation_rubric_audit_logs");

            migrationBuilder.DropTable(
                name: "moderation_rubric_versions");
        }
    }
}
