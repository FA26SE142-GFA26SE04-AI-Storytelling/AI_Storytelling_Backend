-- 33. recommendation_reviews (10)
INSERT INTO recommendation_reviews ("Id", "RecommendationId", "ReviewerUserId", "Decision", "ModifiedValue", "IsFinal", "HadFinalAuthority", "Reason", "ReviewedAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 2, 'Accept', NULL, true, true, 'Đồng ý với đề xuất của hệ thống', NOW() - INTERVAL '7 days', NOW() - INTERVAL '7 days', false),
    (2, 2, 3, 'Modify', '{"readingLevel":4}', false, true, 'Cần điều chỉnh thêm về mức độ khó', NOW() - INTERVAL '6 days', NOW() - INTERVAL '6 days', false),
    (3, 3, 4, 'Accept', NULL, true, true, 'Phù hợp với sở thích của trẻ', NOW() - INTERVAL '5 days', NOW() - INTERVAL '5 days', false),
    (4, 4, 5, 'ObserveMore', NULL, false, false, 'Cần theo dõi thêm một thời gian', NOW() - INTERVAL '4 days', NOW() - INTERVAL '4 days', false),
    (5, 5, 6, 'Accept', NULL, true, true, 'Đồng ý can thiệp sớm', NOW() - INTERVAL '3 days', NOW() - INTERVAL '3 days', false),
    (6, 6, 7, 'Accept', NULL, true, true, 'Giáo viên đồng ý với đề xuất', NOW() - INTERVAL '2 days', NOW() - INTERVAL '2 days', false),
    (7, 7, 8, 'Modify', '{"learningFocus":"vocabulary"}', false, true, 'Đề nghị bổ sung thêm bài tập', NOW() - INTERVAL '1 day', NOW() - INTERVAL '1 day', false),
    (8, 8, 9, 'Accept', NULL, true, true, 'Phù hợp với khả năng của trẻ', NOW() - INTERVAL '1 day', NOW() - INTERVAL '1 day', false),
    (9, 9, 10, 'Reject', NULL, true, true, 'Chưa đủ bằng chứng để can thiệp', NOW() - INTERVAL '1 day', NOW() - INTERVAL '1 day', false),
    (10, 10, 2, 'Accept', NULL, true, true, 'Đồng ý với đề xuất cá nhân hóa', NOW() - INTERVAL '1 day', NOW() - INTERVAL '1 day', false)
ON CONFLICT ("Id") DO NOTHING;
