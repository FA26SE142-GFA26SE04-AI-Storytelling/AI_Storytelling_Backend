using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStoryRatings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StoryRatingId",
                table: "moderation_items",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "story_ratings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoryId = table.Column<int>(type: "integer", nullable: false),
                    ReviewerUserId = table.Column<int>(type: "integer", nullable: false),
                    Rating = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    HiddenReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ModeratedByUserId = table.Column<int>(type: "integer", nullable: true),
                    ModeratedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_story_ratings", x => x.Id);
                    table.CheckConstraint("ck_story_ratings_rating_range", "\"Rating\" BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_story_ratings_stories_StoryId",
                        column: x => x.StoryId,
                        principalTable: "stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_story_ratings_user_accounts_ModeratedByUserId",
                        column: x => x.ModeratedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_story_ratings_user_accounts_ReviewerUserId",
                        column: x => x.ReviewerUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "story_rating_reports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoryRatingId = table.Column<int>(type: "integer", nullable: false),
                    ReporterUserId = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ModerationItemId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_story_rating_reports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_story_rating_reports_moderation_items_ModerationItemId",
                        column: x => x.ModerationItemId,
                        principalTable: "moderation_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_story_rating_reports_story_ratings_StoryRatingId",
                        column: x => x.StoryRatingId,
                        principalTable: "story_ratings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_story_rating_reports_user_accounts_ReporterUserId",
                        column: x => x.ReporterUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_moderation_items_StoryRatingId",
                table: "moderation_items",
                column: "StoryRatingId",
                unique: true,
                filter: "\"ItemType\" = 'StoryRating' AND \"Status\" IN ('Pending', 'Claimed') AND NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_story_rating_reports_ModerationItemId_Status",
                table: "story_rating_reports",
                columns: new[] { "ModerationItemId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_story_rating_reports_ReporterUserId",
                table: "story_rating_reports",
                column: "ReporterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_story_rating_reports_StoryRatingId_ReporterUserId",
                table: "story_rating_reports",
                columns: new[] { "StoryRatingId", "ReporterUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_story_ratings_ModeratedByUserId",
                table: "story_ratings",
                column: "ModeratedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_story_ratings_ReviewerUserId",
                table: "story_ratings",
                column: "ReviewerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_story_ratings_StoryId_ReviewerUserId",
                table: "story_ratings",
                columns: new[] { "StoryId", "ReviewerUserId" },
                unique: true,
                filter: "NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_story_ratings_StoryId_Status",
                table: "story_ratings",
                columns: new[] { "StoryId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_moderation_items_story_ratings_StoryRatingId",
                table: "moderation_items",
                column: "StoryRatingId",
                principalTable: "story_ratings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_moderation_items_story_ratings_StoryRatingId",
                table: "moderation_items");

            migrationBuilder.DropTable(
                name: "story_rating_reports");

            migrationBuilder.DropTable(
                name: "story_ratings");

            migrationBuilder.DropIndex(
                name: "IX_moderation_items_StoryRatingId",
                table: "moderation_items");

            migrationBuilder.DropColumn(
                name: "StoryRatingId",
                table: "moderation_items");
        }
    }
}
