-- 26. assignments (10)
INSERT INTO assignments ("Id", "StoryId", "AssignedByUserId", "ChildProfileId", "ClassGroupId", "AssignedAt", "Status", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 7, NULL, 1, NOW() - INTERVAL '15 days', 'Assigned', NOW() - INTERVAL '15 days', false),
    (2, 2, 8, NULL, 2, NOW() - INTERVAL '14 days', 'InProgress', NOW() - INTERVAL '14 days', false),
    (3, 3, 9, NULL, 3, NOW() - INTERVAL '13 days', 'Completed', NOW() - INTERVAL '13 days', false),
    (4, 4, 10, NULL, 4, NOW() - INTERVAL '12 days', 'Cancelled', NOW() - INTERVAL '12 days', false),
    (5, 5, 7, NULL, 5, NOW() - INTERVAL '11 days', 'Assigned', NOW() - INTERVAL '11 days', false),
    (6, 6, 8, NULL, 1, NOW() - INTERVAL '10 days', 'InProgress', NOW() - INTERVAL '10 days', false),
    (7, 7, 9, NULL, 2, NOW() - INTERVAL '9 days', 'Completed', NOW() - INTERVAL '9 days', false),
    (8, 8, 10, NULL, 3, NOW() - INTERVAL '8 days', 'Assigned', NOW() - INTERVAL '8 days', false),
    (9, 9, 7, NULL, 4, NOW() - INTERVAL '7 days', 'InProgress', NOW() - INTERVAL '7 days', false),
    (10, 10, 8, NULL, 5, NOW() - INTERVAL '6 days', 'Completed', NOW() - INTERVAL '6 days', false)
ON CONFLICT ("Id") DO NOTHING;
