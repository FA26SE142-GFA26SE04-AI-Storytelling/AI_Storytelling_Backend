using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using StoryPlatform.Infrastructure.Persistence;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <summary>
    /// WF-L1A / S8: nhóm tuổi thứ hai đổi từ 9–12 sang 9–10. Chỉ đổi giá trị chuỗi, không đổi cấu trúc bảng.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261013090000_AgeBand9To10")]
    public partial class AgeBand9To10 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE child_profiles SET \"AgeBand\" = 'Age_9_10' WHERE \"AgeBand\" = 'Age_9_12';");
            migrationBuilder.Sql("UPDATE content_categories SET \"DefaultBlockedAgeBands\" = array_replace(\"DefaultBlockedAgeBands\", 'Age_9_12', 'Age_9_10');");
            migrationBuilder.Sql("UPDATE stories SET \"AgeBand\" = 'Age_9_10' WHERE \"AgeBand\" IN ('Age_9_12', '9-12');");
            migrationBuilder.Sql("UPDATE prompt_catalog_versions SET \"GradeBand\" = '9-10' WHERE \"GradeBand\" = '9-12';");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE child_profiles SET \"AgeBand\" = 'Age_9_12' WHERE \"AgeBand\" = 'Age_9_10';");
            migrationBuilder.Sql("UPDATE content_categories SET \"DefaultBlockedAgeBands\" = array_replace(\"DefaultBlockedAgeBands\", 'Age_9_10', 'Age_9_12');");
            migrationBuilder.Sql("UPDATE stories SET \"AgeBand\" = 'Age_9_12' WHERE \"AgeBand\" = 'Age_9_10';");
            migrationBuilder.Sql("UPDATE prompt_catalog_versions SET \"GradeBand\" = '9-12' WHERE \"GradeBand\" = '9-10';");
        }
    }
}
