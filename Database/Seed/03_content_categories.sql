-- 3. content_categories (6)
INSERT INTO content_categories ("Id", "Code", "DisplayName", "IsActive", "CreatedByAdminId", "CreatedAt", "IsDeleted")
VALUES
    (1, 'ANIMALS', 'Dong vat', true, 1, NOW() - INTERVAL '50 days', false),
    (2, 'FANTASY', 'Co tich - Ky ao', true, 1, NOW() - INTERVAL '50 days', false),
    (3, 'ADVENTURE', 'Phieu luu', true, 1, NOW() - INTERVAL '50 days', false),
    (4, 'SCIENCE', 'Khoa hoc', true, 1, NOW() - INTERVAL '50 days', false),
    (5, 'FRIENDSHIP', 'Tinh ban', true, 1, NOW() - INTERVAL '50 days', false),
    (6, 'FAMILY', 'Gia dinh', true, 1, NOW() - INTERVAL '50 days', false)
ON CONFLICT ("Id") DO NOTHING;
