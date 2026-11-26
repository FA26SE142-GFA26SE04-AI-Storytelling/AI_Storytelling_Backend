-- 72. plan_settings (1) - cau hinh chung cua goi va han muc (Luong 5, 5a): ty le quy Creator, muc chi toi thieu cho Creator, han muc thang Creator, luot dung thu, thoi gian doc toi thieu, quyen loi muc Mien phi, gioi han sinh lai trong mot truyen.
INSERT INTO plan_settings ("Id", "ReadingFundPercent", "CreativeFundPercent", "MinimumPayoutVnd", "CreatorMonthlyAiCredits", "CreatorMonthlySubmitCredits", "PartnerMonthlyAiCredits", "PartnerMonthlySubmitCredits", "MinimumReadSeconds", "TrialAiCredits", "FreeMaxChildProfiles", "FreeWeeklyNewLibraryStories", "FreeCanCopyRoadmapTemplates", "FreeReadAloudPerMonth", "FreeWritingAssistPerDay", "CreatorWritingAssistPerDay", "PartnerWritingAssistPerDay", "OutlineRegenerationLimit", "ContentRegenerationLimit", "ImageRegenerationLimit", "AudioRegenerationLimit", "CreatedAt", "IsDeleted")
VALUES
    (1, 30, 10, 200000, 2, 5, 6, 10, 60, 1, 1, 3, false, 5, 5, 20, 40, 3, 2, 3, 2, NOW() - INTERVAL '60 days', false)
ON CONFLICT ("Id") DO NOTHING;
