-- 10. learning_profile_topics (10)
INSERT INTO learning_profile_topics ("Id", "LearningProfileId", "Topic", "Relation", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 'Động vật', 'FavoriteTopic', NOW() - INTERVAL '40 days', false),
    (2, 2, 'Cổ tích', 'FavoriteTopic', NOW() - INTERVAL '39 days', false),
    (3, 3, 'Phiêu lưu', 'PriorityFocusArea', NOW() - INTERVAL '38 days', false),
    (4, 4, 'Khoa học', 'FavoriteTopic', NOW() - INTERVAL '37 days', false),
    (5, 5, 'Tình bạn', 'PriorityFocusArea', NOW() - INTERVAL '36 days', false),
    (6, 6, 'Gia đình', 'FavoriteTopic', NOW() - INTERVAL '35 days', false),
    (7, 7, 'Siêu nhân', 'FavoriteTopic', NOW() - INTERVAL '34 days', false),
    (8, 8, 'Biển cả', 'PriorityFocusArea', NOW() - INTERVAL '33 days', false),
    (9, 9, 'Vũ trụ', 'FavoriteTopic', NOW() - INTERVAL '32 days', false),
    (10, 10, 'Thể thao', 'PriorityFocusArea', NOW() - INTERVAL '18 days', false)
ON CONFLICT ("Id") DO NOTHING;
