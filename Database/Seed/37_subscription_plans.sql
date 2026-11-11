-- 37. subscription_plans (5) - goi Personal cu ngung ban (Legacy); Goi Doc, Goi Sang tao, Add-on cho phu huynh; Goi Creator cho Content Creator. Quyen loi: MaxChildProfiles, WeeklyNewLibraryStories (NULL = khong gioi han), CanCopyRoadmapTemplates, ReadAloudPerMonth, WritingAssistPerDay.
INSERT INTO subscription_plans ("Id", "Name", "PlanType", "Audience", "PriceVnd", "AiCredits", "SubmitCredits", "IsActive", "MaxChildProfiles", "WeeklyNewLibraryStories", "CanCopyRoadmapTemplates", "ReadAloudPerMonth", "WritingAssistPerDay", "CreatedAt", "IsDeleted")
VALUES
    (1, 'Personal', 'Legacy', 'User', 49000, 50, 0, false, 0, NULL, false, NULL, 0, NOW() - INTERVAL '60 days', false),
    (2, 'Gói Đọc', 'Reading', 'User', 49000, 0, 0, true, 4, NULL, true, 60, 5, NOW() - INTERVAL '60 days', false),
    (3, 'Gói Sáng tạo', 'Creative', 'User', 99000, 4, 0, true, 4, NULL, true, 60, 20, NOW() - INTERVAL '60 days', false),
    (4, 'Add-on', 'AddOn', 'User', 29000, 2, 0, true, 0, NULL, false, NULL, 0, NOW() - INTERVAL '60 days', false),
    (5, 'Gói Creator', 'CreatorPack', 'Creator', 99000, 6, 20, true, 0, NULL, false, NULL, 0, NOW() - INTERVAL '60 days', false)
ON CONFLICT ("Id") DO NOTHING;
