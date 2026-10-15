-- 26. assignment_recipients (10)
INSERT INTO assignment_recipients (
    "Id", "AssignmentId", "ChildProfileId", "Status", "CompletedAt", "CancelledByUserId", "CancelledAt", "CreatedAt", "IsDeleted"
)
VALUES
    (1, 1, 2, 'Assigned', NULL, NULL, NULL, NOW() - INTERVAL '15 days', false),
    (2, 2, 4, 'InProgress', NULL, NULL, NULL, NOW() - INTERVAL '14 days', false),
    (3, 3, 10, 'Completed', NOW() - INTERVAL '10 days', NULL, NULL, NOW() - INTERVAL '13 days', false),
    (4, 4, 6, 'Cancelled', NULL, 10, NOW() - INTERVAL '12 days' + INTERVAL '1 day', NOW() - INTERVAL '12 days', false),
    (5, 5, 8, 'Assigned', NULL, NULL, NULL, NOW() - INTERVAL '11 days', false),
    (6, 6, 2, 'InProgress', NULL, NULL, NULL, NOW() - INTERVAL '10 days', false),
    (7, 7, 4, 'Completed', NOW() - INTERVAL '8 days', NULL, NULL, NOW() - INTERVAL '9 days', false),
    (8, 8, 6, 'Assigned', NULL, NULL, NULL, NOW() - INTERVAL '8 days', false),
    (9, 9, 8, 'InProgress', NULL, NULL, NULL, NOW() - INTERVAL '7 days', false),
    (10, 10, 10, 'Completed', NOW() - INTERVAL '5 days', NULL, NULL, NOW() - INTERVAL '6 days', false)
ON CONFLICT ("Id") DO NOTHING;
