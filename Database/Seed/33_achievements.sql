-- 33. achievements (10 - unique theo ChildProfileId)
INSERT INTO achievements ("Id", "ChildProfileId", "ExpTotal", "StreakDays", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 120, 5, NOW() - INTERVAL '10 days', false),
    (2, 2, 80, 2, NOW() - INTERVAL '9 days', false),
    (3, 3, 200, 8, NOW() - INTERVAL '8 days', false),
    (4, 4, 150, 4, NOW() - INTERVAL '7 days', false),
    (5, 5, 60, 1, NOW() - INTERVAL '6 days', false),
    (6, 6, 90, 3, NOW() - INTERVAL '5 days', false),
    (7, 7, 40, 1, NOW() - INTERVAL '4 days', false),
    (8, 8, 250, 10, NOW() - INTERVAL '3 days', false),
    (9, 9, 70, 2, NOW() - INTERVAL '2 days', false),
    (10, 10, 180, 6, NOW() - INTERVAL '1 days', false)
ON CONFLICT ("Id") DO NOTHING;
