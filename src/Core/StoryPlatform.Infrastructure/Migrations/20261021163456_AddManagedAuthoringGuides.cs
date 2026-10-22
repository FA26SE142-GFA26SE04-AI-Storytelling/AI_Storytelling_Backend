using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddManagedAuthoringGuides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "authoring_guide_catalog_versions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CatalogId = table.Column<int>(type: "integer", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false),
                    CatalogVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ContentJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    PublishedByUserId = table.Column<int>(type: "integer", nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_authoring_guide_catalog_versions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "authoring_guide_catalogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Locale = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    AudienceRole = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PublishedVersionId = table.Column<int>(type: "integer", nullable: true),
                    DraftVersionId = table.Column<int>(type: "integer", nullable: true),
                    RevisionToken = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_authoring_guide_catalogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_authoring_guide_catalogs_authoring_guide_catalog_versions_D~",
                        column: x => x.DraftVersionId,
                        principalTable: "authoring_guide_catalog_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_authoring_guide_catalogs_authoring_guide_catalog_versions_P~",
                        column: x => x.PublishedVersionId,
                        principalTable: "authoring_guide_catalog_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_authoring_guide_catalog_versions_CatalogId",
                table: "authoring_guide_catalog_versions",
                column: "CatalogId",
                unique: true,
                filter: "\"Status\" = 'Draft' AND NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_authoring_guide_catalog_versions_CatalogId_CatalogVersion",
                table: "authoring_guide_catalog_versions",
                columns: new[] { "CatalogId", "CatalogVersion" },
                unique: true,
                filter: "NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_authoring_guide_catalog_versions_CatalogId_VersionNo",
                table: "authoring_guide_catalog_versions",
                columns: new[] { "CatalogId", "VersionNo" },
                unique: true,
                filter: "NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_authoring_guide_catalogs_DraftVersionId",
                table: "authoring_guide_catalogs",
                column: "DraftVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_authoring_guide_catalogs_Locale_AudienceRole",
                table: "authoring_guide_catalogs",
                columns: new[] { "Locale", "AudienceRole" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_authoring_guide_catalogs_PublishedVersionId",
                table: "authoring_guide_catalogs",
                column: "PublishedVersionId");

            migrationBuilder.AddForeignKey(
                name: "FK_authoring_guide_catalog_versions_authoring_guide_catalogs_C~",
                table: "authoring_guide_catalog_versions",
                column: "CatalogId",
                principalTable: "authoring_guide_catalogs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_authoring_guide_catalog_versions_authoring_guide_catalogs_C~",
                table: "authoring_guide_catalog_versions");

            migrationBuilder.DropTable(
                name: "authoring_guide_catalogs");

            migrationBuilder.DropTable(
                name: "authoring_guide_catalog_versions");
        }
    }
}
