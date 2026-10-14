using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using StoryPlatform.Infrastructure.Persistence;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <summary>
    /// S3 Mục 11: LoggedIn / LoggedOut / EmailVerified gộp thành Active. Chỉ đổi giá trị chuỗi.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261013091000_AccountStatusActive")]
    public partial class AccountStatusActive : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE user_accounts SET \"Status\" = 'Active' WHERE \"Status\" IN ('LoggedIn', 'LoggedOut', 'EmailVerified');");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Không khôi phục được giá trị cũ; Active vẫn đọc được bằng enum bản trước nếu thêm lại thủ công.
        }
    }
}
