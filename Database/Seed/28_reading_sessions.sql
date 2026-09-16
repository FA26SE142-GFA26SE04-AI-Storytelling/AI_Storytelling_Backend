-- 28. reading_sessions (10)
INSERT INTO reading_sessions ("Id", "ChildProfileId", "StoryId", "AssignmentRecipientId", "StartedAt", "CompletedAt", "Status", "PagesCompleted", "TimeSpentSeconds", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 1, NULL, NOW() - INTERVAL '10 days', NOW() - INTERVAL '10 days' + INTERVAL '10 minutes', 'Completed', 10, 600, NOW() - INTERVAL '10 days', false),
    (2, 2, 1, 1, NOW() - INTERVAL '9 days', NULL, 'Reading', 3, 200, NOW() - INTERVAL '9 days', false),
    (3, 3, 3, NULL, NOW() - INTERVAL '8 days', NOW() - INTERVAL '8 days' + INTERVAL '8 minutes', 'Completed', 8, 480, NOW() - INTERVAL '8 days', false),
    (4, 4, 4, 2, NOW() - INTERVAL '7 days', NULL, 'Activity', 5, 350, NOW() - INTERVAL '7 days', false),
    (5, 5, 5, NULL, NOW() - INTERVAL '6 days', NOW() - INTERVAL '6 days' + INTERVAL '6 minutes', 'Completed', 6, 360, NOW() - INTERVAL '6 days', false),
    (6, 6, 6, NULL, NOW() - INTERVAL '5 days', NULL, 'Started', 1, 60, NOW() - INTERVAL '5 days', false),
    (7, 7, 7, NULL, NOW() - INTERVAL '4 days', NULL, 'Abandoned', 2, 90, NOW() - INTERVAL '4 days', false),
    (8, 8, 8, 5, NOW() - INTERVAL '3 days', NOW() - INTERVAL '3 days' + INTERVAL '12 minutes', 'Completed', 12, 720, NOW() - INTERVAL '3 days', false),
    (9, 9, 9, NULL, NOW() - INTERVAL '2 days', NULL, 'Reading', 4, 240, NOW() - INTERVAL '2 days', false),
    (10, 10, 10, NULL, NOW() - INTERVAL '1 days', NOW() - INTERVAL '1 days' + INTERVAL '9 minutes', 'Completed', 9, 540, NOW() - INTERVAL '1 days', false)
ON CONFLICT ("Id") DO NOTHING;
