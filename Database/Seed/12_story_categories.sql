-- 12. story_categories (15)
INSERT INTO story_categories ("Id", "StoryId", "ContentCategoryId", "Source", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 1, 'Ai', NOW() - INTERVAL '30 days', false),
    (2, 2, 2, 'Ai', NOW() - INTERVAL '28 days', false),
    (3, 3, 3, 'Ai', NOW() - INTERVAL '26 days', false),
    (4, 4, 4, 'Moderator', NOW() - INTERVAL '24 days', false),
    (5, 5, 5, 'Parent', NOW() - INTERVAL '22 days', false),
    (6, 6, 6, 'Ai', NOW() - INTERVAL '20 days', false),
    (7, 7, 1, 'Ai', NOW() - INTERVAL '19 days', false),
    (8, 8, 2, 'Ai', NOW() - INTERVAL '17 days', false),
    (9, 9, 3, 'Ai', NOW() - INTERVAL '15 days', false),
    (10, 10, 4, 'Ai', NOW() - INTERVAL '13 days', false),
    (11, 6, 4, 'Moderator', NOW() - INTERVAL '12 days', false),
    (12, 11, 1, 'Moderator', NOW() - INTERVAL '1 day', false),
    (13, 13, 1, 'Moderator', NOW() - INTERVAL '6 days', false),
    (14, 14, 1, 'Moderator', NOW() - INTERVAL '7 days', false),
    (15, 19, 1, 'Ai', NOW() - INTERVAL '12 days', false)
ON CONFLICT ("Id") DO NOTHING;
