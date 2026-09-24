-- 43. recommendation_reviews (10)
INSERT INTO recommendation_reviews ("Id", "RecommendationId", "ReviewerUserId", "Decision", "ModifiedValue", "IsFinal", "HadFinalAuthority", "Reason", "ReviewedAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 2, 'Accept', NULL, true, true, 'Dong y voi de xuat cua he thong', NOW() - INTERVAL '7 days', NOW() - INTERVAL '7 days', false),
    (2, 2, 3, 'Modify', '{"readingLevel":4}', false, true, 'Can dieu chinh them ve muc do kho', NOW() - INTERVAL '6 days', NOW() - INTERVAL '6 days', false),
    (3, 3, 4, 'Accept', NULL, true, true, 'Phu hop voi so thich cua tre', NOW() - INTERVAL '5 days', NOW() - INTERVAL '5 days', false),
    (4, 4, 5, 'ObserveMore', NULL, false, false, 'Can theo doi them mot thoi gian', NOW() - INTERVAL '4 days', NOW() - INTERVAL '4 days', false),
    (5, 5, 6, 'Accept', NULL, true, true, 'Dong y can thiep som', NOW() - INTERVAL '3 days', NOW() - INTERVAL '3 days', false),
    (6, 6, 7, 'Accept', NULL, true, true, 'Giao vien dong y voi de xuat', NOW() - INTERVAL '2 days', NOW() - INTERVAL '2 days', false),
    (7, 7, 8, 'Modify', '{"learningFocus":"vocabulary"}', false, true, 'De nghi bo sung them bai tap', NOW() - INTERVAL '1 day', NOW() - INTERVAL '1 day', false),
    (8, 8, 9, 'Accept', NULL, true, true, 'Phu hop voi kha nang cua tre', NOW() - INTERVAL '1 day', NOW() - INTERVAL '1 day', false),
    (9, 9, 10, 'Reject', NULL, true, true, 'Chua du bang chung de can thiep', NOW() - INTERVAL '1 day', NOW() - INTERVAL '1 day', false),
    (10, 10, 2, 'Accept', NULL, true, true, 'Dong y voi de xuat ca nhan hoa', NOW() - INTERVAL '1 day', NOW() - INTERVAL '1 day', false)
ON CONFLICT ("Id") DO NOTHING;
