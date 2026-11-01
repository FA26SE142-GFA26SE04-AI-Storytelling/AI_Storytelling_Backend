using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddListeningInteractions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_story_generation_jobs_Operation_StoryVersionId",
                table: "story_generation_jobs");

            migrationBuilder.AddColumn<string>(
                name: "ListeningInteractionType",
                table: "story_generation_jobs",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ListenOnly",
                table: "reading_progress",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AudioPartKey",
                table: "media_assets",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ListeningInteractionId",
                table: "media_assets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ListeningInteractionOptionId",
                table: "media_assets",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "listening_interactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoryVersionId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AnchorOffset = table.Column<int>(type: "integer", nullable: false),
                    StorySceneId = table.Column<int>(type: "integer", nullable: true),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Prompt = table.Column<string>(type: "text", nullable: true),
                    SourceQuote = table.Column<string>(type: "text", nullable: true),
                    SourceStart = table.Column<int>(type: "integer", nullable: true),
                    SourceEnd = table.Column<int>(type: "integer", nullable: true),
                    CharacterName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    HiddenWord = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Hint = table.Column<string>(type: "text", nullable: true),
                    RevealOffset = table.Column<int>(type: "integer", nullable: true),
                    Explanation = table.Column<string>(type: "text", nullable: true),
                    MatchFeedback = table.Column<string>(type: "text", nullable: true),
                    NoMatchFeedback = table.Column<string>(type: "text", nullable: true),
                    WaitFeedback = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_listening_interactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_listening_interactions_story_scenes_StorySceneId",
                        column: x => x.StorySceneId,
                        principalTable: "story_scenes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_listening_interactions_story_versions_StoryVersionId",
                        column: x => x.StoryVersionId,
                        principalTable: "story_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "listening_interaction_options",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ListeningInteractionId = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    Feedback = table.Column<string>(type: "text", nullable: true),
                    IsMatch = table.Column<bool>(type: "boolean", nullable: false),
                    CorrectOrder = table.Column<int>(type: "integer", nullable: true),
                    SourceQuote = table.Column<string>(type: "text", nullable: true),
                    SourceStart = table.Column<int>(type: "integer", nullable: true),
                    SourceEnd = table.Column<int>(type: "integer", nullable: true),
                    ImageDescription = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_listening_interaction_options", x => x.Id);
                    table.ForeignKey(
                        name: "FK_listening_interaction_options_listening_interactions_Listen~",
                        column: x => x.ListeningInteractionId,
                        principalTable: "listening_interactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "listening_interaction_attempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReadingSessionId = table.Column<int>(type: "integer", nullable: false),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: false),
                    ListeningInteractionId = table.Column<int>(type: "integer", nullable: false),
                    SelectedOptionId = table.Column<int>(type: "integer", nullable: true),
                    AnswerText = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OrderedOptionIdsJson = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsMatch = table.Column<bool>(type: "boolean", nullable: true),
                    AttemptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_listening_interaction_attempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_listening_interaction_attempts_child_profiles_ChildProfileId",
                        column: x => x.ChildProfileId,
                        principalTable: "child_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_listening_interaction_attempts_listening_interaction_option~",
                        column: x => x.SelectedOptionId,
                        principalTable: "listening_interaction_options",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_listening_interaction_attempts_listening_interactions_Liste~",
                        column: x => x.ListeningInteractionId,
                        principalTable: "listening_interactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_listening_interaction_attempts_reading_sessions_ReadingSess~",
                        column: x => x.ReadingSessionId,
                        principalTable: "reading_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_story_generation_jobs_Operation_StoryVersionId",
                table: "story_generation_jobs",
                columns: new[] { "Operation", "StoryVersionId" },
                unique: true,
                filter: "\"StoryVersionId\" IS NOT NULL AND \"IsDeleted\" = false AND \"Operation\" <> 'GenerateListeningInteraction'");

            migrationBuilder.CreateIndex(
                name: "IX_story_generation_jobs_StoryVersionId_ListeningInteractionTy~",
                table: "story_generation_jobs",
                columns: new[] { "StoryVersionId", "ListeningInteractionType" },
                unique: true,
                filter: "\"Operation\" = 'GenerateListeningInteraction' AND \"StoryVersionId\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_ListeningInteractionId",
                table: "media_assets",
                column: "ListeningInteractionId",
                filter: "\"ListeningInteractionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_ListeningInteractionOptionId",
                table: "media_assets",
                column: "ListeningInteractionOptionId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_media_assets_interaction_required_for_listening_audio",
                table: "media_assets",
                sql: "\"Type\" <> 'ListeningAudio' OR \"ListeningInteractionId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_media_assets_option_required_for_listening_image",
                table: "media_assets",
                sql: "\"Type\" <> 'ListeningImage' OR \"ListeningInteractionOptionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_listening_interaction_attempts_ChildProfileId",
                table: "listening_interaction_attempts",
                column: "ChildProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_listening_interaction_attempts_ListeningInteractionId",
                table: "listening_interaction_attempts",
                column: "ListeningInteractionId");

            migrationBuilder.CreateIndex(
                name: "IX_listening_interaction_attempts_ReadingSessionId_ListeningIn~",
                table: "listening_interaction_attempts",
                columns: new[] { "ReadingSessionId", "ListeningInteractionId" });

            migrationBuilder.CreateIndex(
                name: "IX_listening_interaction_attempts_SelectedOptionId",
                table: "listening_interaction_attempts",
                column: "SelectedOptionId");

            migrationBuilder.CreateIndex(
                name: "IX_listening_interaction_options_ListeningInteractionId_SortOr~",
                table: "listening_interaction_options",
                columns: new[] { "ListeningInteractionId", "SortOrder" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_listening_interactions_StorySceneId",
                table: "listening_interactions",
                column: "StorySceneId");

            migrationBuilder.CreateIndex(
                name: "IX_listening_interactions_StoryVersionId_AnchorOffset",
                table: "listening_interactions",
                columns: new[] { "StoryVersionId", "AnchorOffset" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_listening_interactions_StoryVersionId_Type",
                table: "listening_interactions",
                columns: new[] { "StoryVersionId", "Type" });

            migrationBuilder.AddForeignKey(
                name: "FK_media_assets_listening_interaction_options_ListeningInterac~",
                table: "media_assets",
                column: "ListeningInteractionOptionId",
                principalTable: "listening_interaction_options",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_media_assets_listening_interactions_ListeningInteractionId",
                table: "media_assets",
                column: "ListeningInteractionId",
                principalTable: "listening_interactions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_media_assets_listening_interaction_options_ListeningInterac~",
                table: "media_assets");

            migrationBuilder.DropForeignKey(
                name: "FK_media_assets_listening_interactions_ListeningInteractionId",
                table: "media_assets");

            migrationBuilder.DropTable(
                name: "listening_interaction_attempts");

            migrationBuilder.DropTable(
                name: "listening_interaction_options");

            migrationBuilder.DropTable(
                name: "listening_interactions");

            migrationBuilder.DropIndex(
                name: "IX_story_generation_jobs_Operation_StoryVersionId",
                table: "story_generation_jobs");

            migrationBuilder.DropIndex(
                name: "IX_story_generation_jobs_StoryVersionId_ListeningInteractionTy~",
                table: "story_generation_jobs");

            migrationBuilder.DropIndex(
                name: "IX_media_assets_ListeningInteractionId",
                table: "media_assets");

            migrationBuilder.DropIndex(
                name: "IX_media_assets_ListeningInteractionOptionId",
                table: "media_assets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_media_assets_interaction_required_for_listening_audio",
                table: "media_assets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_media_assets_option_required_for_listening_image",
                table: "media_assets");

            migrationBuilder.DropColumn(
                name: "ListeningInteractionType",
                table: "story_generation_jobs");

            migrationBuilder.DropColumn(
                name: "ListenOnly",
                table: "reading_progress");

            migrationBuilder.DropColumn(
                name: "AudioPartKey",
                table: "media_assets");

            migrationBuilder.DropColumn(
                name: "ListeningInteractionId",
                table: "media_assets");

            migrationBuilder.DropColumn(
                name: "ListeningInteractionOptionId",
                table: "media_assets");

            migrationBuilder.CreateIndex(
                name: "IX_story_generation_jobs_Operation_StoryVersionId",
                table: "story_generation_jobs",
                columns: new[] { "Operation", "StoryVersionId" },
                unique: true,
                filter: "\"StoryVersionId\" IS NOT NULL AND \"IsDeleted\" = false");
        }
    }
}
