using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChildRoadmapItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ChildRoadmapItemId",
                table: "reading_sessions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "child_roadmap_items",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: false),
                    StoryId = table.Column<int>(type: "integer", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "Waiting"),
                    SkipReason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "Manual"),
                    SourceReadingRoadmapId = table.Column<int>(type: "integer", nullable: true),
                    AddedByUserId = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ExpectedDays = table.Column<int>(type: "integer", nullable: true),
                    ExpectedCompletionAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OpenedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SkippedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RemovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RemovedByUserId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_child_roadmap_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_child_roadmap_items_child_profiles_ChildProfileId",
                        column: x => x.ChildProfileId,
                        principalTable: "child_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_child_roadmap_items_reading_roadmaps_SourceReadingRoadmapId",
                        column: x => x.SourceReadingRoadmapId,
                        principalTable: "reading_roadmaps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_child_roadmap_items_stories_StoryId",
                        column: x => x.StoryId,
                        principalTable: "stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_child_roadmap_items_user_accounts_AddedByUserId",
                        column: x => x.AddedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_child_roadmap_items_user_accounts_RemovedByUserId",
                        column: x => x.RemovedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_reading_sessions_ChildRoadmapItemId",
                table: "reading_sessions",
                column: "ChildRoadmapItemId");

            migrationBuilder.CreateIndex(
                name: "IX_child_roadmap_items_AddedByUserId",
                table: "child_roadmap_items",
                column: "AddedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_child_roadmap_items_ChildProfileId",
                table: "child_roadmap_items",
                column: "ChildProfileId",
                unique: true,
                filter: "\"Status\" = 'Open' AND NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_child_roadmap_items_ChildProfileId_Status_Position",
                table: "child_roadmap_items",
                columns: new[] { "ChildProfileId", "Status", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_child_roadmap_items_ChildProfileId_StoryId",
                table: "child_roadmap_items",
                columns: new[] { "ChildProfileId", "StoryId" },
                unique: true,
                filter: "\"Status\" IN ('Waiting', 'Open') AND NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_child_roadmap_items_RemovedByUserId",
                table: "child_roadmap_items",
                column: "RemovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_child_roadmap_items_SourceReadingRoadmapId",
                table: "child_roadmap_items",
                column: "SourceReadingRoadmapId");

            migrationBuilder.CreateIndex(
                name: "IX_child_roadmap_items_StoryId",
                table: "child_roadmap_items",
                column: "StoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_reading_sessions_child_roadmap_items_ChildRoadmapItemId",
                table: "reading_sessions",
                column: "ChildRoadmapItemId",
                principalTable: "child_roadmap_items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_reading_sessions_child_roadmap_items_ChildRoadmapItemId",
                table: "reading_sessions");

            migrationBuilder.DropTable(
                name: "child_roadmap_items");

            migrationBuilder.DropIndex(
                name: "IX_reading_sessions_ChildRoadmapItemId",
                table: "reading_sessions");

            migrationBuilder.DropColumn(
                name: "ChildRoadmapItemId",
                table: "reading_sessions");
        }
    }
}
