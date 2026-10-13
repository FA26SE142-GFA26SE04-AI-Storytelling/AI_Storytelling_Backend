-- 3. content_categories (8)
INSERT INTO content_categories ("Id", "Code", "DisplayName", "IsActive", "CreatedByAdminId", "DefaultBlockedAgeBands", "CreatedAt", "IsDeleted")
VALUES
    (1, 'ANIMALS', 'Dong vat', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (2, 'FANTASY', 'Co tich - Ky ao', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (3, 'ADVENTURE', 'Phieu luu', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (4, 'SCIENCE', 'Khoa hoc', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (5, 'FRIENDSHIP', 'Tinh ban', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (6, 'FAMILY', 'Gia dinh', true, 1, '{}', NOW() - INTERVAL '50 days', false),
    (7, 'VIOLENCE', 'Bao luc', true, 1, '{Age_6_8,Age_9_10}', NOW() - INTERVAL '50 days', false),
    (8, 'SCARY_CONTENT', 'Noi dung doi', true, 1, '{Age_6_8}', NOW() - INTERVAL '50 days', false)
ON CONFLICT ("Id") DO NOTHING;
