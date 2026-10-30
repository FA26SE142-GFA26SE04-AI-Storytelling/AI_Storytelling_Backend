-- 3. content_categories (8)
INSERT INTO content_categories ("Id", "Code", "DisplayName", "IsActive", "CreatedByAdminId", "DefaultBlockedAgeBands", "CreatedAt", "IsDeleted")
VALUES
    (1, 'ANIMALS', 'Động vật', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (2, 'FANTASY', 'Cổ tích - Kỳ ảo', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (3, 'ADVENTURE', 'Phiêu lưu', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (4, 'SCIENCE', 'Khoa học', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (5, 'FRIENDSHIP', 'Tình bạn', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (6, 'FAMILY', 'Gia đình', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (7, 'VIOLENCE', 'Bạo lực', true, 1, '{Age_6_8,Age_9_10}', NOW() - INTERVAL '50 days', false),
    (8, 'SCARY_CONTENT', 'Nội dung đáng sợ', true, 1, '{Age_6_8}', NOW() - INTERVAL '50 days', false)
ON CONFLICT ("Id") DO NOTHING;
