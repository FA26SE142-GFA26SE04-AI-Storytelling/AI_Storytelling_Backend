-- 16. learning_profile_topics (10)
INSERT INTO learning_profile_topics ("Id", "LearningProfileId", "Topic", "Relation", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 'Dong vat', 'FavoriteTopic', NOW() - INTERVAL '40 days', false),
    (2, 2, 'Co tich', 'FavoriteTopic', NOW() - INTERVAL '39 days', false),
    (3, 3, 'Phieu luu', 'PriorityFocusArea', NOW() - INTERVAL '38 days', false),
    (4, 4, 'Khoa hoc', 'FavoriteTopic', NOW() - INTERVAL '37 days', false),
    (5, 5, 'Tinh ban', 'PriorityFocusArea', NOW() - INTERVAL '36 days', false),
    (6, 6, 'Gia dinh', 'FavoriteTopic', NOW() - INTERVAL '35 days', false),
    (7, 7, 'Sieu nhan', 'FavoriteTopic', NOW() - INTERVAL '34 days', false),
    (8, 8, 'Bien ca', 'PriorityFocusArea', NOW() - INTERVAL '33 days', false),
    (9, 9, 'Vu tru', 'FavoriteTopic', NOW() - INTERVAL '32 days', false),
    (10, 10, 'The thao', 'PriorityFocusArea', NOW() - INTERVAL '18 days', false)
ON CONFLICT ("Id") DO NOTHING;
