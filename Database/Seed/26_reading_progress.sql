-- 26. reading_progress (10)
INSERT INTO reading_progress ("Id", "ChildProfileId", "StoryId", "LastPageRead", "IsBookmarked", "IsFavorited", "ListenOnly", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 1, 10, true, true, false, NOW() - INTERVAL '10 days', false),
    (2, 2, 1, 3, false, false, false, NOW() - INTERVAL '9 days', false),
    (3, 3, 3, 8, true, true, false, NOW() - INTERVAL '8 days', false),
    (4, 4, 4, 5, false, true, false, NOW() - INTERVAL '7 days', false),
    (5, 5, 5, 6, true, false, false, NOW() - INTERVAL '6 days', false),
    (6, 6, 6, 1, false, false, false, NOW() - INTERVAL '5 days', false),
    (7, 7, 7, 2, false, false, false, NOW() - INTERVAL '4 days', false),
    (8, 8, 8, 12, true, true, false, NOW() - INTERVAL '3 days', false),
    (9, 9, 9, 4, false, true, false, NOW() - INTERVAL '2 days', false),
    (10, 10, 10, 9, true, true, false, NOW() - INTERVAL '1 days', false)
ON CONFLICT ("Id") DO NOTHING;
