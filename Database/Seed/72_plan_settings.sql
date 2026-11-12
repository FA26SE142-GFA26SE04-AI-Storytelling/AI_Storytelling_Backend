-- 72. plan_settings (1) - cau hinh chung cua goi va han muc (Luong 5, 5a): ty le quy Creator, han muc thang Creator, luot dung thu, thoi gian doc toi thieu, quyen loi muc Mien phi.
INSERT INTO plan_settings ("Id", "ReadingFundPercent", "CreativeFundPercent", "CreatorMonthlyAiCredits", "CreatorMonthlySubmitCredits", "PartnerMonthlyAiCredits", "PartnerMonthlySubmitCredits", "MinimumReadSeconds", "TrialAiCredits", "FreeMaxChildProfiles", "FreeWeeklyNewLibraryStories", "FreeCanCopyRoadmapTemplates", "FreeReadAloudPerMonth", "FreeWritingAssistPerDay", "CreatorWritingAssistPerDay", "PartnerWritingAssistPerDay", "CreatedAt", "IsDeleted")
VALUES
    (1, 30, 10, 2, 5, 6, 10, 60, 1, 1, 3, false, 5, 5, 20, 40, NOW() - INTERVAL '60 days', false)
ON CONFLICT ("Id") DO NOTHING;
