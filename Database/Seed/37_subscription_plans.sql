-- 37. subscription_plans (4) - goi Personal cu ngung ban (Legacy); Goi Doc, Goi Sang tao, Add-on theo Luong 2.
INSERT INTO subscription_plans ("Id", "Name", "PlanType", "PriceVnd", "AiCredits", "IsActive", "CreatedAt", "IsDeleted")
VALUES
    (1, 'Personal', 'Legacy', 49000, 50, false, NOW() - INTERVAL '60 days', false),
    (2, 'Gói Đọc', 'Reading', 49000, 0, true, NOW() - INTERVAL '60 days', false),
    (3, 'Gói Sáng tạo', 'Creative', 99000, 4, true, NOW() - INTERVAL '60 days', false),
    (4, 'Add-on', 'AddOn', 29000, 2, true, NOW() - INTERVAL '60 days', false)
ON CONFLICT ("Id") DO NOTHING;
