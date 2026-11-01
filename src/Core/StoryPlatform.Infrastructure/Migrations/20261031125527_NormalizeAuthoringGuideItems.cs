using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeAuthoringGuideItems : Migration
    {
        /// <summary>Chép mục và chỗ trống từ <c>ContentJson</c> sang hai bảng; bỏ qua phiên bản đã có dòng nên chạy lại vẫn an toàn.</summary>
        public const string BackfillSql = """
            WITH src AS (
                SELECT v."Id" AS version_id, v."CreatedAt" AS created_at, e AS item
                FROM authoring_guide_catalog_versions v
                CROSS JOIN LATERAL jsonb_array_elements(COALESCE(v."ContentJson"->'items', '[]'::jsonb)) AS e
                WHERE v."ContentJson" IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM authoring_guide_items i WHERE i."CatalogVersionId" = v."Id")
            ), inserted AS (
                INSERT INTO authoring_guide_items ("CatalogVersionId", "GuideKey", "ItemType", "SupportGroup", "Purposes",
                    "Title", "Instruction", "Example", "TemplateText", "Order", "CreatedAt", "IsDeleted")
                SELECT src.version_id, src.item->>'id', src.item->>'itemType', src.item->>'supportGroup',
                    ARRAY(SELECT jsonb_array_elements_text(COALESCE(src.item->'purposes', '[]'::jsonb))),
                    src.item->>'title', src.item->>'instruction', src.item->>'example', src.item->>'templateText',
                    (src.item->>'order')::integer, src.created_at, false
                FROM src
                RETURNING "Id", "CatalogVersionId", "GuideKey", "CreatedAt"
            )
            INSERT INTO authoring_guide_placeholders ("ItemId", "Key", "Label", "Hint", "Order", "CreatedAt", "IsDeleted")
            SELECT ins."Id", p.key, NULL, NULL, p.ord::integer, ins."CreatedAt", false
            FROM inserted ins
            JOIN src ON src.version_id = ins."CatalogVersionId" AND src.item->>'id' = ins."GuideKey"
            CROSS JOIN LATERAL jsonb_array_elements_text(COALESCE(src.item->'placeholderDefinitions', '[]'::jsonb))
                WITH ORDINALITY AS p(key, ord);

            DO $$
            BEGIN
                IF (SELECT count(*) FROM authoring_guide_items i
                        JOIN authoring_guide_catalog_versions v ON v."Id" = i."CatalogVersionId"
                        WHERE v."ContentJson" IS NOT NULL)
                   <> (SELECT COALESCE(sum(jsonb_array_length(COALESCE("ContentJson"->'items', '[]'::jsonb))), 0)
                        FROM authoring_guide_catalog_versions WHERE "ContentJson" IS NOT NULL)
                THEN
                    RAISE EXCEPTION 'authoring guide backfill row counts do not match';
                END IF;
            END $$;
            """;

        /// <summary>Định dạng JSON cũ chỉ có tên biến nên rollback sẽ làm mất nhãn/gợi ý; chặn lại thay vì âm thầm mất dữ liệu.</summary>
        public const string RollbackGuardSql = """
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM authoring_guide_placeholders
                           WHERE NOT "IsDeleted" AND ("Label" IS NOT NULL OR "Hint" IS NOT NULL))
                THEN
                    RAISE EXCEPTION 'rollback would lose placeholder labels';
                END IF;
            END $$;
            """;

        /// <summary>Dựng lại <c>ContentJson</c> (schema 1) cho mọi phiên bản từ hai bảng.</summary>
        public const string RollbackSql = """
            UPDATE authoring_guide_catalog_versions AS v
            SET "ContentJson" = jsonb_build_object('schemaVersion', 1, 'items', COALESCE((
                SELECT jsonb_agg(jsonb_build_object(
                    'id', i."GuideKey", 'audienceRole', c."AudienceRole", 'purposes', to_jsonb(i."Purposes"),
                    'supportGroup', i."SupportGroup", 'itemType', i."ItemType", 'title', i."Title",
                    'instruction', i."Instruction", 'example', i."Example", 'templateText', i."TemplateText",
                    'placeholderDefinitions', COALESCE((
                        SELECT jsonb_agg(p."Key" ORDER BY p."Order")
                        FROM authoring_guide_placeholders p
                        WHERE p."ItemId" = i."Id" AND NOT p."IsDeleted"), '[]'::jsonb),
                    'order', i."Order") ORDER BY i."Order", i."GuideKey")
                FROM authoring_guide_items i
                WHERE i."CatalogVersionId" = v."Id" AND NOT i."IsDeleted"), '[]'::jsonb))
            FROM authoring_guide_catalogs c
            WHERE c."Id" = v."CatalogId";
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ContentJson",
                table: "authoring_guide_catalog_versions",
                type: "jsonb",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "jsonb");

            migrationBuilder.CreateTable(
                name: "authoring_guide_items",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CatalogVersionId = table.Column<int>(type: "integer", nullable: false),
                    GuideKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ItemType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SupportGroup = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Purposes = table.Column<List<string>>(type: "text[]", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Instruction = table.Column<string>(type: "text", nullable: false),
                    Example = table.Column<string>(type: "text", nullable: true),
                    TemplateText = table.Column<string>(type: "text", nullable: true),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_authoring_guide_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_authoring_guide_items_authoring_guide_catalog_versions_Cata~",
                        column: x => x.CatalogVersionId,
                        principalTable: "authoring_guide_catalog_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "authoring_guide_placeholders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    Key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Hint = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_authoring_guide_placeholders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_authoring_guide_placeholders_authoring_guide_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "authoring_guide_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_authoring_guide_items_CatalogVersionId_GuideKey",
                table: "authoring_guide_items",
                columns: new[] { "CatalogVersionId", "GuideKey" },
                unique: true,
                filter: "NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_authoring_guide_placeholders_ItemId_Key",
                table: "authoring_guide_placeholders",
                columns: new[] { "ItemId", "Key" },
                unique: true,
                filter: "NOT \"IsDeleted\"");

            migrationBuilder.Sql(BackfillSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RollbackGuardSql);
            migrationBuilder.Sql(RollbackSql);

            migrationBuilder.AlterColumn<string>(
                name: "ContentJson",
                table: "authoring_guide_catalog_versions",
                type: "jsonb",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);

            migrationBuilder.DropTable(
                name: "authoring_guide_placeholders");

            migrationBuilder.DropTable(
                name: "authoring_guide_items");
        }
    }
}
