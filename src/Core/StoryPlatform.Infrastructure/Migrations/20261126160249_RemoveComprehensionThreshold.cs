using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveComprehensionThreshold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ComprehensionThresholdPercent",
                table: "safety_policies");

            migrationBuilder.DropColumn(
                name: "ComprehensionWindowSize",
                table: "safety_policies");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ComprehensionThresholdPercent",
                table: "safety_policies",
                type: "numeric(5,2)",
                nullable: false,
                defaultValue: 70m);

            migrationBuilder.AddColumn<int>(
                name: "ComprehensionWindowSize",
                table: "safety_policies",
                type: "integer",
                nullable: false,
                defaultValue: 3);
        }
    }
}
