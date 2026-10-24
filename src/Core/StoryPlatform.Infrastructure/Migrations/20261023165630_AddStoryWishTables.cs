using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStoryWishTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wish_cards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Group = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ImageUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    AudioUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AgeBand = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ContentCategoryId = table.Column<int>(type: "integer", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wish_cards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_wish_cards_content_categories_ContentCategoryId",
                        column: x => x.ContentCategoryId,
                        principalTable: "content_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "story_wish_card_matches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoryId = table.Column<int>(type: "integer", nullable: false),
                    WishCardId = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_story_wish_card_matches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_story_wish_card_matches_stories_StoryId",
                        column: x => x.StoryId,
                        principalTable: "stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_story_wish_card_matches_wish_cards_WishCardId",
                        column: x => x.WishCardId,
                        principalTable: "wish_cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "story_wishes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CharacterCardId = table.Column<int>(type: "integer", nullable: true),
                    PlaceCardId = table.Column<int>(type: "integer", nullable: true),
                    ChallengeCardId = table.Column<int>(type: "integer", nullable: true),
                    LessonCardId = table.Column<int>(type: "integer", nullable: true),
                    LessonLeftToParent = table.Column<bool>(type: "boolean", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PendingSince = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FulfilledStoryId = table.Column<int>(type: "integer", nullable: true),
                    FulfilledWithExistingStory = table.Column<bool>(type: "boolean", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_story_wishes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_story_wishes_child_profiles_ChildProfileId",
                        column: x => x.ChildProfileId,
                        principalTable: "child_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_story_wishes_stories_FulfilledStoryId",
                        column: x => x.FulfilledStoryId,
                        principalTable: "stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_story_wishes_wish_cards_ChallengeCardId",
                        column: x => x.ChallengeCardId,
                        principalTable: "wish_cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_story_wishes_wish_cards_CharacterCardId",
                        column: x => x.CharacterCardId,
                        principalTable: "wish_cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_story_wishes_wish_cards_LessonCardId",
                        column: x => x.LessonCardId,
                        principalTable: "wish_cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_story_wishes_wish_cards_PlaceCardId",
                        column: x => x.PlaceCardId,
                        principalTable: "wish_cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "story_wish_suggestions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoryWishId = table.Column<int>(type: "integer", nullable: false),
                    StoryId = table.Column<int>(type: "integer", nullable: false),
                    CommunitySubmissionId = table.Column<int>(type: "integer", nullable: true),
                    Rank = table.Column<int>(type: "integer", nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    MatchedCardIds = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_story_wish_suggestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_story_wish_suggestions_stories_StoryId",
                        column: x => x.StoryId,
                        principalTable: "stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_story_wish_suggestions_story_wishes_StoryWishId",
                        column: x => x.StoryWishId,
                        principalTable: "story_wishes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_story_wish_card_matches_StoryId_WishCardId",
                table: "story_wish_card_matches",
                columns: new[] { "StoryId", "WishCardId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_story_wish_card_matches_WishCardId",
                table: "story_wish_card_matches",
                column: "WishCardId");

            migrationBuilder.CreateIndex(
                name: "IX_story_wish_suggestions_StoryId",
                table: "story_wish_suggestions",
                column: "StoryId");

            migrationBuilder.CreateIndex(
                name: "IX_story_wish_suggestions_StoryWishId_Rank",
                table: "story_wish_suggestions",
                columns: new[] { "StoryWishId", "Rank" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_story_wish_suggestions_StoryWishId_StoryId",
                table: "story_wish_suggestions",
                columns: new[] { "StoryWishId", "StoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_story_wishes_ChallengeCardId",
                table: "story_wishes",
                column: "ChallengeCardId");

            migrationBuilder.CreateIndex(
                name: "IX_story_wishes_CharacterCardId",
                table: "story_wishes",
                column: "CharacterCardId");

            migrationBuilder.CreateIndex(
                name: "IX_story_wishes_child_status_submitted",
                table: "story_wishes",
                columns: new[] { "ChildProfileId", "Status", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_story_wishes_FulfilledStoryId",
                table: "story_wishes",
                column: "FulfilledStoryId");

            migrationBuilder.CreateIndex(
                name: "IX_story_wishes_LessonCardId",
                table: "story_wishes",
                column: "LessonCardId");

            migrationBuilder.CreateIndex(
                name: "IX_story_wishes_one_draft_per_child",
                table: "story_wishes",
                column: "ChildProfileId",
                unique: true,
                filter: "\"Status\" = 'Draft' AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_story_wishes_PlaceCardId",
                table: "story_wishes",
                column: "PlaceCardId");

            migrationBuilder.CreateIndex(
                name: "IX_wish_cards_ContentCategoryId",
                table: "wish_cards",
                column: "ContentCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_wish_cards_Group_IsActive_SortOrder",
                table: "wish_cards",
                columns: new[] { "Group", "IsActive", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "story_wish_card_matches");

            migrationBuilder.DropTable(
                name: "story_wish_suggestions");

            migrationBuilder.DropTable(
                name: "story_wishes");

            migrationBuilder.DropTable(
                name: "wish_cards");
        }
    }
}
