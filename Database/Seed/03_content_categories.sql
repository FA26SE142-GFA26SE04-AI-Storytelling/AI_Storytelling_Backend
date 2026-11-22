-- 3. content_categories (40)
-- Cot "Group": Genre (The loai), LifeValue (Gia tri va ky nang song), Learning (Hoc tap), SafetyWarning (Canh bao an toan).
-- Nhan nhom "Canh bao an toan" co DefaultBlockedAgeBands se thanh preset Blocked
-- khi tao chinh sach an toan moi, va Code/DisplayName thanh tu khoa chan cho nhom tuoi do.
-- BULLYING khong chan mac dinh: truyen chong bat nat la bai hoc hop le, phu huynh tu chan neu muon.
INSERT INTO content_categories ("Id", "Code", "DisplayName", "Group", "IsActive", "CreatedByAdminId", "DefaultBlockedAgeBands", "CreatedAt", "IsDeleted")
VALUES
    (1, 'ANIMALS', 'Động vật', 'Genre', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (2, 'FANTASY', 'Cổ tích - Kỳ ảo', 'Genre', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (3, 'ADVENTURE', 'Phiêu lưu', 'Genre', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (4, 'SCIENCE', 'Khoa học', 'Learning', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (5, 'FRIENDSHIP', 'Tình bạn', 'LifeValue', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (6, 'FAMILY', 'Gia đình', 'Genre', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (7, 'VIOLENCE', 'Bạo lực', 'SafetyWarning', true, 1, '{Age_6_8,Age_9_10}', NOW() - INTERVAL '50 days', false),
    (8, 'SCARY_CONTENT', 'Nội dung đáng sợ', 'SafetyWarning', true, 1, '{Age_6_8}', NOW() - INTERVAL '50 days', false),
    (9, 'HISTORY', 'Lịch sử', 'Genre', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (10, 'VIETNAMESE_FOLKTALES', 'Truyện dân gian Việt Nam', 'Genre', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (11, 'FABLES', 'Ngụ ngôn', 'Genre', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (12, 'DAILY_LIFE', 'Đời sống hằng ngày', 'Genre', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (13, 'NATURE_ENVIRONMENT', 'Thiên nhiên - Môi trường', 'Genre', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (14, 'MYSTERY_DETECTIVE', 'Bí ẩn - Phá án', 'Genre', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (15, 'HUMOR', 'Hài hước', 'Genre', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (16, 'SPORTS_ACTIVITY', 'Thể thao - Vận động', 'Genre', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (17, 'SPACE', 'Vũ trụ', 'Genre', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (18, 'BEDTIME_STORIES', 'Truyện trước giờ ngủ', 'Genre', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (19, 'COURAGE', 'Lòng dũng cảm', 'LifeValue', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (20, 'HONESTY', 'Trung thực', 'LifeValue', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (21, 'KINDNESS_SHARING', 'Lòng tốt - Sẻ chia', 'LifeValue', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (22, 'RESPONSIBILITY', 'Trách nhiệm', 'LifeValue', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (23, 'HUMILITY_LISTENING', 'Khiêm tốn - Biết lắng nghe', 'LifeValue', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (24, 'PERSEVERANCE', 'Kiên trì', 'LifeValue', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (25, 'TEAMWORK', 'Hợp tác - Làm việc nhóm', 'LifeValue', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (26, 'EMOTIONS_SELF_AWARENESS', 'Cảm xúc - Hiểu bản thân', 'LifeValue', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (27, 'GRATITUDE', 'Biết ơn', 'LifeValue', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (28, 'RESPECT_ELDERS', 'Lễ phép - Kính trọng người lớn', 'LifeValue', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (29, 'SCHOOL', 'Trường học', 'Learning', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (30, 'MATH', 'Toán học', 'Learning', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (31, 'VIETNAMESE_VOCABULARY', 'Tiếng Việt - Từ vựng', 'Learning', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (32, 'CULTURE_FESTIVALS', 'Văn hóa - Lễ hội', 'Learning', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (33, 'HEALTH_HYGIENE', 'Sức khỏe - Vệ sinh', 'Learning', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (34, 'SAFETY_SKILLS', 'Kỹ năng an toàn', 'Learning', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (35, 'CAREERS', 'Nghề nghiệp', 'Learning', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (36, 'LOSS_DEATH', 'Mất mát - Cái chết', 'SafetyWarning', true, 1, '{Age_6_8}', NOW() - INTERVAL '50 days', false),
    (37, 'WAR_CONFLICT', 'Chiến tranh - Xung đột', 'SafetyWarning', true, 1, '{Age_6_8}', NOW() - INTERVAL '50 days', false),
    (38, 'BULLYING', 'Bắt nạt', 'SafetyWarning', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (39, 'DANGEROUS_IMITABLE_BEHAVIOR', 'Hành vi nguy hiểm dễ bắt chước', 'SafetyWarning', true, 1, '{Age_6_8,Age_9_10}', NOW() - INTERVAL '50 days', false),
    (40, 'PROFANITY', 'Ngôn ngữ thô tục', 'SafetyWarning', true, 1, '{Age_6_8,Age_9_10}', NOW() - INTERVAL '50 days', false)
ON CONFLICT ("Id") DO NOTHING;
