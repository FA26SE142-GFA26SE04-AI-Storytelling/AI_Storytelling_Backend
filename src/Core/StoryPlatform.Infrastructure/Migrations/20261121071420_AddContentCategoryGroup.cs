using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddContentCategoryGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Group",
                table: "content_categories",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Genre");

            // Nhãn đã có trong DB mặc định là Genre; gán nhóm khác theo mã nhãn đã biết.
            // Nhãn Admin tự tạo với mã khác vẫn là Genre và Admin có thể đổi sau.
            migrationBuilder.Sql("""
                UPDATE content_categories SET "Group" = 'LifeValue'
                WHERE "Code" IN ('FRIENDSHIP', 'COURAGE', 'HONESTY', 'KINDNESS_SHARING', 'RESPONSIBILITY',
                    'HUMILITY_LISTENING', 'PERSEVERANCE', 'TEAMWORK', 'EMOTIONS_SELF_AWARENESS', 'GRATITUDE',
                    'RESPECT_ELDERS');
                UPDATE content_categories SET "Group" = 'Learning'
                WHERE "Code" IN ('SCIENCE', 'SCHOOL', 'MATH', 'VIETNAMESE_VOCABULARY', 'CULTURE_FESTIVALS',
                    'HEALTH_HYGIENE', 'SAFETY_SKILLS', 'CAREERS');
                UPDATE content_categories SET "Group" = 'SafetyWarning'
                WHERE "Code" IN ('VIOLENCE', 'SCARY_CONTENT', 'LOSS_DEATH', 'WAR_CONFLICT', 'BULLYING',
                    'DANGEROUS_IMITABLE_BEHAVIOR', 'PROFANITY');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Group",
                table: "content_categories");
        }
    }
}
