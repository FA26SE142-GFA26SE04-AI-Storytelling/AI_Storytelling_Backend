-- 36. intervention_cases (10)
INSERT INTO intervention_cases (
    "Id", "ChildProfileId", "RecommendationId", "TriggerType", "Status",
    "TriggeringStoryId", "ResolutionType", "OpenedAt", "ResolvedAt", "ResolvedByUserId",
    "SkillGapNotes", "NotesAddedByUserId", "NotesAddedAt", "CreatedAt", "IsDeleted"
)
VALUES
    (1, 1, NULL, 'AutoBelowThreshold', 'OpenHoldMode', 1, NULL, NOW() - INTERVAL '6 days', NULL, NULL,
     'Cần theo dõi thêm về tốc độ đọc', 2, NOW() - INTERVAL '6 days' + INTERVAL '1 hour', NOW() - INTERVAL '6 days', false),
    (2, 2, 2, 'RecommendedByAi', 'UnderReview', NULL, NULL, NOW() - INTERVAL '5 days', NULL, NULL,
     'Cần bổ sung bài tập từ vựng', 2, NOW() - INTERVAL '5 days' + INTERVAL '1 hour', NOW() - INTERVAL '5 days', false),
    (3, 5, 5, 'AutoBelowThreshold', 'OpenHoldMode', 5, NULL, NOW() - INTERVAL '4 days', NULL, NULL,
     'Tỷ lệ hoàn thành bài giao quá thấp', 4, NOW() - INTERVAL '4 days' + INTERVAL '1 hour', NOW() - INTERVAL '4 days', false),
    (4, 7, 7, 'RecommendedByAi', 'UnderReview', NULL, NULL, NOW() - INTERVAL '3 days', NULL, NULL,
     'Lỗi phát âm lặp lại nhiều lần', 5, NOW() - INTERVAL '3 days' + INTERVAL '1 hour', NOW() - INTERVAL '3 days', false),
    (5, 9, 9, 'AutoBelowThreshold', 'ResolvedUnlocked', 9, 'AutoRetakePass', NOW() - INTERVAL '10 days', NOW() - INTERVAL '2 days', NULL,
     'Đã cải thiện sau khi retake truyện gây kích hoạt Hold Mode', 6, NOW() - INTERVAL '9 days', NOW() - INTERVAL '10 days', false),
    (6, 3, NULL, 'RecommendedByAi', 'ResolvedUnlocked', NULL, 'ManualParent', NOW() - INTERVAL '12 days', NOW() - INTERVAL '9 days', 3,
     'Đã giải quyết, trẻ tiến bộ tốt', 3, NOW() - INTERVAL '11 days', NOW() - INTERVAL '12 days', false),
    (7, 4, NULL, 'AutoBelowThreshold', 'OpenHoldMode', 4, NULL, NOW() - INTERVAL '2 days', NULL, NULL,
     'Theo dõi khả năng suy luận', 3, NOW() - INTERVAL '2 days' + INTERVAL '1 hour', NOW() - INTERVAL '2 days', false),
    (8, 6, NULL, 'RecommendedByAi', 'ResolvedUnlocked', NULL, 'ManualParent', NOW() - INTERVAL '1 day', NOW() - INTERVAL '2 hours', 4,
     'Đánh giá thêm về tốc độ tiến bộ', 4, NOW() - INTERVAL '23 hours', NOW() - INTERVAL '1 day', false),
    (9, 8, NULL, 'AutoBelowThreshold', 'ResolvedUnlocked', 8, 'AutoRetakePass', NOW() - INTERVAL '8 days', NOW() - INTERVAL '3 days', NULL,
     'Trẻ đã vượt qua khó khăn ban đầu sau lần đọc lại', 5, NOW() - INTERVAL '7 days', NOW() - INTERVAL '8 days', false),
    (10, 10, NULL, 'RecommendedByAi', 'OpenHoldMode', NULL, NULL, NOW() - INTERVAL '1 day', NULL, NULL,
     'Cần quan sát thêm về sở thích đọc', 6, NOW() - INTERVAL '23 hours', NOW() - INTERVAL '1 day', false)
ON CONFLICT ("Id") DO NOTHING;
