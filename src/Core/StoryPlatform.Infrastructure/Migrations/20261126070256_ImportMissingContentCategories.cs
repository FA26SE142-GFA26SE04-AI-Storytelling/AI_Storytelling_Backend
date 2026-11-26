using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoryPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ImportMissingContentCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nhãn 9-40 chỉ được thêm vào seed (03_content_categories.sql), mà seed chỉ chạy cho database mới,
            // nên database đã có dữ liệu (prod) chỉ có 8 nhãn đầu. Chỉ chạy khi bảng đã có nhãn: với database mới,
            // seed chạy sau migration sẽ tự thêm đủ 40 nhãn kèm Id. Bỏ qua mã đã có, kể cả nhãn đã bị xoá mềm.
            migrationBuilder.Sql(
                """
                INSERT INTO content_categories ("Code", "DisplayName", "Group", "IsActive", "DefaultBlockedAgeBands", "CreatedAt", "IsDeleted")
                SELECT v.code, v.name, v.grp, TRUE, v.blocked, NOW(), FALSE
                FROM (VALUES
                    ('HISTORY', 'Lịch sử', 'Genre', '{}'::text[]),
                    ('VIETNAMESE_FOLKTALES', 'Truyện dân gian Việt Nam', 'Genre', '{}'::text[]),
                    ('FABLES', 'Ngụ ngôn', 'Genre', '{}'::text[]),
                    ('DAILY_LIFE', 'Đời sống hằng ngày', 'Genre', '{}'::text[]),
                    ('NATURE_ENVIRONMENT', 'Thiên nhiên - Môi trường', 'Genre', '{}'::text[]),
                    ('MYSTERY_DETECTIVE', 'Bí ẩn - Phá án', 'Genre', '{}'::text[]),
                    ('HUMOR', 'Hài hước', 'Genre', '{}'::text[]),
                    ('SPORTS_ACTIVITY', 'Thể thao - Vận động', 'Genre', '{}'::text[]),
                    ('SPACE', 'Vũ trụ', 'Genre', '{}'::text[]),
                    ('BEDTIME_STORIES', 'Truyện trước giờ ngủ', 'Genre', '{}'::text[]),
                    ('COURAGE', 'Lòng dũng cảm', 'LifeValue', '{}'::text[]),
                    ('HONESTY', 'Trung thực', 'LifeValue', '{}'::text[]),
                    ('KINDNESS_SHARING', 'Lòng tốt - Sẻ chia', 'LifeValue', '{}'::text[]),
                    ('RESPONSIBILITY', 'Trách nhiệm', 'LifeValue', '{}'::text[]),
                    ('HUMILITY_LISTENING', 'Khiêm tốn - Biết lắng nghe', 'LifeValue', '{}'::text[]),
                    ('PERSEVERANCE', 'Kiên trì', 'LifeValue', '{}'::text[]),
                    ('TEAMWORK', 'Hợp tác - Làm việc nhóm', 'LifeValue', '{}'::text[]),
                    ('EMOTIONS_SELF_AWARENESS', 'Cảm xúc - Hiểu bản thân', 'LifeValue', '{}'::text[]),
                    ('GRATITUDE', 'Biết ơn', 'LifeValue', '{}'::text[]),
                    ('RESPECT_ELDERS', 'Lễ phép - Kính trọng người lớn', 'LifeValue', '{}'::text[]),
                    ('SCHOOL', 'Trường học', 'Learning', '{}'::text[]),
                    ('MATH', 'Toán học', 'Learning', '{}'::text[]),
                    ('VIETNAMESE_VOCABULARY', 'Tiếng Việt - Từ vựng', 'Learning', '{}'::text[]),
                    ('CULTURE_FESTIVALS', 'Văn hóa - Lễ hội', 'Learning', '{}'::text[]),
                    ('HEALTH_HYGIENE', 'Sức khỏe - Vệ sinh', 'Learning', '{}'::text[]),
                    ('SAFETY_SKILLS', 'Kỹ năng an toàn', 'Learning', '{}'::text[]),
                    ('CAREERS', 'Nghề nghiệp', 'Learning', '{}'::text[]),
                    ('LOSS_DEATH', 'Mất mát - Cái chết', 'SafetyWarning', '{Age_6_8}'::text[]),
                    ('WAR_CONFLICT', 'Chiến tranh - Xung đột', 'SafetyWarning', '{Age_6_8}'::text[]),
                    ('BULLYING', 'Bắt nạt', 'SafetyWarning', '{}'::text[]),
                    ('DANGEROUS_IMITABLE_BEHAVIOR', 'Hành vi nguy hiểm dễ bắt chước', 'SafetyWarning', '{Age_6_8,Age_9_10}'::text[]),
                    ('PROFANITY', 'Ngôn ngữ thô tục', 'SafetyWarning', '{Age_6_8,Age_9_10}'::text[])
                ) AS v(code, name, grp, blocked)
                WHERE EXISTS (SELECT 1 FROM content_categories)
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Không xoá: nhãn có thể đã được gán cho truyện hoặc chính sách an toàn sau khi thêm.
        }
    }
}
