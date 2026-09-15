-- 42. shared_story_recipients (10)
INSERT INTO shared_story_recipients ("Id", "SharedStoryId", "RecipientUserId", "Status", "RespondedAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 2, 'Accepted', NOW() - INTERVAL '13 days', NOW() - INTERVAL '14 days', false),
    (2, 2, 3, 'Pending', NULL, NOW() - INTERVAL '14 days', false),
    (3, 3, 4, 'Accepted', NOW() - INTERVAL '11 days', NOW() - INTERVAL '12 days', false),
    (4, 4, 5, 'Declined', NOW() - INTERVAL '10 days', NOW() - INTERVAL '11 days', false),
    (5, 5, 6, 'Accepted', NOW() - INTERVAL '9 days', NOW() - INTERVAL '10 days', false),
    (6, 6, 2, 'Pending', NULL, NOW() - INTERVAL '9 days', false),
    (7, 7, 3, 'Accepted', NOW() - INTERVAL '7 days', NOW() - INTERVAL '8 days', false),
    (8, 8, 4, 'Accepted', NOW() - INTERVAL '6 days', NOW() - INTERVAL '7 days', false),
    (9, 9, 5, 'Revoked', NOW() - INTERVAL '5 days', NOW() - INTERVAL '6 days', false),
    (10, 10, 6, 'Accepted', NOW() - INTERVAL '4 days', NOW() - INTERVAL '5 days', false)
ON CONFLICT ("Id") DO NOTHING;
