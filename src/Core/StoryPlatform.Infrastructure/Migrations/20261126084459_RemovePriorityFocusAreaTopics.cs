using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemovePriorityFocusAreaTopics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // "Ky nang muon uu tien" da bi bo khoi TopicRelation (khong noi nao dung khi sinh truyen).
            // Dong cu co Relation nay khong doc duoc voi enum hien tai nen xoa han.
            migrationBuilder.Sql(
                """
                DELETE FROM learning_profile_topics WHERE "Relation" = 'PriorityFocusArea';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Khong khoi phuc: cac dong nay khong doc duoc voi enum hien tai.
        }
    }
}
