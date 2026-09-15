-- 17. story_categories (10)
INSERT INTO story_categories ("Id", "StoryId", "ContentCategoryId", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 1, NOW() - INTERVAL '30 days', false),
    (2, 2, 2, NOW() - INTERVAL '28 days', false),
    (3, 3, 3, NOW() - INTERVAL '26 days', false),
    (4, 4, 4, NOW() - INTERVAL '24 days', false),
    (5, 5, 5, NOW() - INTERVAL '22 days', false),
    (6, 6, 6, NOW() - INTERVAL '20 days', false),
    (7, 7, 1, NOW() - INTERVAL '19 days', false),
    (8, 8, 2, NOW() - INTERVAL '17 days', false),
    (9, 9, 3, NOW() - INTERVAL '15 days', false),
    (10, 10, 4, NOW() - INTERVAL '13 days', false)
ON CONFLICT ("Id") DO NOTHING;
