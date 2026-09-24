-- 47. shared_stories (10)
INSERT INTO shared_stories ("Id", "StoryId", "ClassGroupId", "SharedByUserId", "ShareMode", "TeacherStatus", "ReviewedByUserId", "ReviewedAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 1, 7, 'Broadcast', 'Approved', 7, NOW() - INTERVAL '14 days', NOW() - INTERVAL '15 days', false),
    (2, 2, 2, 8, 'OneToOne', 'Pending', NULL, NULL, NOW() - INTERVAL '14 days', false),
    (3, 3, 3, 9, 'Broadcast', 'Approved', 9, NOW() - INTERVAL '12 days', NOW() - INTERVAL '13 days', false),
    (4, 4, 4, 10, 'OneToOne', 'Rejected', 10, NOW() - INTERVAL '11 days', NOW() - INTERVAL '12 days', false),
    (5, 5, 5, 7, 'Broadcast', 'Approved', 7, NOW() - INTERVAL '10 days', NOW() - INTERVAL '11 days', false),
    (6, 6, 1, 8, 'Broadcast', 'Pending', NULL, NULL, NOW() - INTERVAL '10 days', false),
    (7, 7, 2, 9, 'OneToOne', 'Approved', 9, NOW() - INTERVAL '8 days', NOW() - INTERVAL '9 days', false),
    (8, 8, 3, 10, 'Broadcast', 'Approved', 10, NOW() - INTERVAL '7 days', NOW() - INTERVAL '8 days', false),
    (9, 9, 4, 7, 'OneToOne', 'Rejected', 7, NOW() - INTERVAL '6 days', NOW() - INTERVAL '7 days', false),
    (10, 10, 5, 8, 'Broadcast', 'Approved', 8, NOW() - INTERVAL '5 days', NOW() - INTERVAL '6 days', false)
ON CONFLICT ("Id") DO NOTHING;
