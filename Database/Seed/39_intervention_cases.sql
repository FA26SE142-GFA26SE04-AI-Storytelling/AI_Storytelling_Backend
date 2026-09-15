-- 39. intervention_cases (10)
INSERT INTO intervention_cases ("Id", "ChildProfileId", "RecommendationId", "TriggerType", "Status", "OpenedAt", "ResolvedAt", "ResolvedByUserId", "SkillGapNotes", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, NULL, 'AutoBelowThreshold', 'OpenHoldMode', NOW() - INTERVAL '6 days', NULL, NULL, 'Can theo doi them ve toc do doc', NOW() - INTERVAL '6 days', false),
    (2, 2, 2, 'RecommendedByAi', 'UnderReview', NOW() - INTERVAL '5 days', NULL, NULL, 'Can bo sung bai tap tu vung', NOW() - INTERVAL '5 days', false),
    (3, 5, 5, 'AutoBelowThreshold', 'OpenHoldMode', NOW() - INTERVAL '4 days', NULL, NULL, 'Ty le hoan thanh bai giao qua thap', NOW() - INTERVAL '4 days', false),
    (4, 7, 7, 'RecommendedByAi', 'UnderReview', NOW() - INTERVAL '3 days', NULL, NULL, 'Loi phat am lap lai nhieu lan', NOW() - INTERVAL '3 days', false),
    (5, 9, 9, 'AutoBelowThreshold', 'ResolvedUnlocked', NOW() - INTERVAL '10 days', NOW() - INTERVAL '2 days', 3, 'Da cai thien sau khi nhac nho', NOW() - INTERVAL '10 days', false),
    (6, 3, NULL, 'RecommendedByAi', 'ResolvedUnlocked', NOW() - INTERVAL '12 days', NOW() - INTERVAL '9 days', 4, 'Da giai quyet, tre tien bo tot', NOW() - INTERVAL '12 days', false),
    (7, 4, NULL, 'AutoBelowThreshold', 'OpenHoldMode', NOW() - INTERVAL '2 days', NULL, NULL, 'Theo doi kha nang suy luan', NOW() - INTERVAL '2 days', false),
    (8, 6, NULL, 'RecommendedByAi', 'UnderReview', NOW() - INTERVAL '1 days', NULL, NULL, 'Danh gia them ve toc do tien bo', NOW() - INTERVAL '1 days', false),
    (9, 8, NULL, 'AutoBelowThreshold', 'ResolvedUnlocked', NOW() - INTERVAL '8 days', NOW() - INTERVAL '3 days', 9, 'Tre da vuot qua kho khan ban dau', NOW() - INTERVAL '8 days', false),
    (10, 10, NULL, 'RecommendedByAi', 'OpenHoldMode', NOW() - INTERVAL '1 days', NULL, NULL, 'Can quan sat them ve so thich doc', NOW() - INTERVAL '1 days', false)
ON CONFLICT ("Id") DO NOTHING;
