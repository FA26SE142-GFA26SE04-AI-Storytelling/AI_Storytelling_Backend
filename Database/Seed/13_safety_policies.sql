-- 13. safety_policies (10 - unique theo ChildProfileId)
INSERT INTO safety_policies ("Id", "ChildProfileId", "MaxStoryLength", "ParentalGateEnabled", "ConsentRecorded", "ConsentRecordedAt", "RequiredApprovalMode", "ConsentPolicyVersion", "SafetyScoreThreshold", "ReadabilityScoreThreshold", "ComprehensionThresholdPercent", "ComprehensionWindowSize", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 1500, true, true, NOW() - INTERVAL '40 days', 'AlwaysManual', 1, NULL, NULL, 70.00, 3, NOW() - INTERVAL '40 days', false),
    (2, 2, 1800, true, true, NOW() - INTERVAL '39 days', 'AutoPublishOnThreshold', 1, 85.00, 70.00, 75.00, 3, NOW() - INTERVAL '39 days', false),
    (3, 3, 1500, true, true, NOW() - INTERVAL '38 days', 'AlwaysManual', 1, NULL, NULL, 70.00, 3, NOW() - INTERVAL '38 days', false),
    (4, 4, 2000, true, true, NOW() - INTERVAL '37 days', 'AutoPublishOnThreshold', 1, 88.00, 72.00, 70.00, 3, NOW() - INTERVAL '37 days', false),
    (5, 5, 1200, true, false, NULL, 'AlwaysManual', 1, NULL, NULL, 70.00, 3, NOW() - INTERVAL '36 days', false),
    (6, 6, 1800, true, true, NOW() - INTERVAL '35 days', 'AlwaysManual', 1, NULL, NULL, 70.00, 3, NOW() - INTERVAL '35 days', false),
    (7, 7, 1500, true, true, NOW() - INTERVAL '34 days', 'AlwaysManual', 1, NULL, NULL, 70.00, 3, NOW() - INTERVAL '34 days', false),
    (8, 8, 2000, true, true, NOW() - INTERVAL '33 days', 'AutoPublishOnThreshold', 1, 90.00, 75.00, 80.00, 3, NOW() - INTERVAL '33 days', false),
    (9, 9, 1000, false, false, NULL, 'AlwaysManual', 1, NULL, NULL, 70.00, 3, NOW() - INTERVAL '32 days', false),
    (10, 10, 1800, true, false, NULL, 'AlwaysManual', 1, NULL, NULL, 70.00, 3, NOW() - INTERVAL '18 days', false)
ON CONFLICT ("Id") DO NOTHING;
