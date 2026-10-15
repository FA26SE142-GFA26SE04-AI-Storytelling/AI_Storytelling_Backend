using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UnifyCreatorAuthoringWithStories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_stories_AuthorUserId",
                table: "stories");

            migrationBuilder.AddColumn<int>(
                name: "AudienceRevision",
                table: "story_versions",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "AuthoringOrigin",
                table: "story_versions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BaseVersionId",
                table: "story_versions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImportFileName",
                table: "story_versions",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImportPreviewHash",
                table: "story_versions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImportSourceHash",
                table: "story_versions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperationId",
                table: "story_versions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperationResponseJson",
                table: "story_versions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "story_versions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RestoredFromVersionId",
                table: "story_versions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RightsDeclaration",
                table: "story_versions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SourceDeclaration",
                table: "story_versions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<int>(
                name: "ChildProfileId",
                table: "stories",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "AudienceRevision",
                table: "stories",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "AuthoringLastVersionNo",
                table: "stories",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "AuthoringRevisionToken",
                table: "stories",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CreateOperationId",
                table: "stories",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreateRequestHash",
                table: "stories",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreateResponseJson",
                table: "stories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExperienceMode",
                table: "stories",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "reading");

            migrationBuilder.AddColumn<string>(
                name: "Scope",
                table: "stories",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Parent");

            // Existing stories belong to the Parent workflow. Initialize authoring
            // metadata without changing their content, current version or publication state.
            migrationBuilder.Sql("""
                UPDATE stories AS s
                SET "AuthoringRevisionToken" = md5(s."Id"::text || random()::text || clock_timestamp()::text),
                    "AuthoringLastVersionNo" = COALESCE(
                        (SELECT MAX(v."VersionNo") FROM story_versions AS v WHERE v."StoryId" = s."Id"), 0);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_story_versions_BaseVersionId",
                table: "story_versions",
                column: "BaseVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_story_versions_RestoredFromVersionId",
                table: "story_versions",
                column: "RestoredFromVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_story_versions_StoryId_ImportPreviewHash",
                table: "story_versions",
                columns: new[] { "StoryId", "ImportPreviewHash" },
                unique: true,
                filter: "\"ImportPreviewHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_story_versions_StoryId_OperationId",
                table: "story_versions",
                columns: new[] { "StoryId", "OperationId" },
                unique: true,
                filter: "\"OperationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_stories_AuthorUserId_CreateOperationId",
                table: "stories",
                columns: new[] { "AuthorUserId", "CreateOperationId" },
                unique: true,
                filter: "\"CreateOperationId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Story_ScopeChild",
                table: "stories",
                sql: "(\"Scope\" = 'Parent' AND \"ChildProfileId\" IS NOT NULL AND \"ChildProfileId\" > 0) OR (\"Scope\" = 'Creator' AND \"ChildProfileId\" IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_story_versions_story_versions_BaseVersionId",
                table: "story_versions",
                column: "BaseVersionId",
                principalTable: "story_versions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_story_versions_story_versions_RestoredFromVersionId",
                table: "story_versions",
                column: "RestoredFromVersionId",
                principalTable: "story_versions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The previous schema requires a child for every story. Refuse a
            // rollback that would assign an invalid child or discard Creator data.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM stories WHERE "ChildProfileId" IS NULL) THEN
                        RAISE EXCEPTION 'Cannot roll back Creator authoring while stories without a child profile exist.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_story_versions_story_versions_BaseVersionId",
                table: "story_versions");

            migrationBuilder.DropForeignKey(
                name: "FK_story_versions_story_versions_RestoredFromVersionId",
                table: "story_versions");

            migrationBuilder.DropIndex(
                name: "IX_story_versions_BaseVersionId",
                table: "story_versions");

            migrationBuilder.DropIndex(
                name: "IX_story_versions_RestoredFromVersionId",
                table: "story_versions");

            migrationBuilder.DropIndex(
                name: "IX_story_versions_StoryId_ImportPreviewHash",
                table: "story_versions");

            migrationBuilder.DropIndex(
                name: "IX_story_versions_StoryId_OperationId",
                table: "story_versions");

            migrationBuilder.DropIndex(
                name: "IX_stories_AuthorUserId_CreateOperationId",
                table: "stories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Story_ScopeChild",
                table: "stories");

            migrationBuilder.DropColumn(
                name: "AudienceRevision",
                table: "story_versions");

            migrationBuilder.DropColumn(
                name: "AuthoringOrigin",
                table: "story_versions");

            migrationBuilder.DropColumn(
                name: "BaseVersionId",
                table: "story_versions");

            migrationBuilder.DropColumn(
                name: "ImportFileName",
                table: "story_versions");

            migrationBuilder.DropColumn(
                name: "ImportPreviewHash",
                table: "story_versions");

            migrationBuilder.DropColumn(
                name: "ImportSourceHash",
                table: "story_versions");

            migrationBuilder.DropColumn(
                name: "OperationId",
                table: "story_versions");

            migrationBuilder.DropColumn(
                name: "OperationResponseJson",
                table: "story_versions");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "story_versions");

            migrationBuilder.DropColumn(
                name: "RestoredFromVersionId",
                table: "story_versions");

            migrationBuilder.DropColumn(
                name: "RightsDeclaration",
                table: "story_versions");

            migrationBuilder.DropColumn(
                name: "SourceDeclaration",
                table: "story_versions");

            migrationBuilder.DropColumn(
                name: "AudienceRevision",
                table: "stories");

            migrationBuilder.DropColumn(
                name: "AuthoringLastVersionNo",
                table: "stories");

            migrationBuilder.DropColumn(
                name: "AuthoringRevisionToken",
                table: "stories");

            migrationBuilder.DropColumn(
                name: "CreateOperationId",
                table: "stories");

            migrationBuilder.DropColumn(
                name: "CreateRequestHash",
                table: "stories");

            migrationBuilder.DropColumn(
                name: "CreateResponseJson",
                table: "stories");

            migrationBuilder.DropColumn(
                name: "ExperienceMode",
                table: "stories");

            migrationBuilder.DropColumn(
                name: "Scope",
                table: "stories");

            migrationBuilder.AlterColumn<int>(
                name: "ChildProfileId",
                table: "stories",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_stories_AuthorUserId",
                table: "stories",
                column: "AuthorUserId");
        }
    }
}
