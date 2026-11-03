using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKnowledgeTreesAndNotebookRemovedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RemovedAt",
                table: "vocabulary_notebook_entries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "knowledge_trees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: false),
                    ExpInvested = table.Column<int>(type: "integer", nullable: false),
                    CurrentTreeExp = table.Column<int>(type: "integer", nullable: false),
                    FloweredTreeCount = table.Column<int>(type: "integer", nullable: false),
                    LastWateredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_trees", x => x.Id);
                    table.CheckConstraint("CK_knowledge_trees_CurrentTreeExp", "\"CurrentTreeExp\" >= 0");
                    table.CheckConstraint("CK_knowledge_trees_ExpInvested", "\"ExpInvested\" >= 0");
                    table.CheckConstraint("CK_knowledge_trees_FloweredTreeCount", "\"FloweredTreeCount\" >= 0");
                    table.ForeignKey(
                        name: "FK_knowledge_trees_child_profiles_ChildProfileId",
                        column: x => x.ChildProfileId,
                        principalTable: "child_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_trees_ChildProfileId",
                table: "knowledge_trees",
                column: "ChildProfileId",
                unique: true,
                filter: "\"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "knowledge_trees");

            migrationBuilder.DropColumn(
                name: "RemovedAt",
                table: "vocabulary_notebook_entries");
        }
    }
}
