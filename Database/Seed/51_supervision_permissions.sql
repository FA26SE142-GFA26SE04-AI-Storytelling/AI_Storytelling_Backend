-- 51. supervision_permissions
INSERT INTO supervision_permissions ("Id", "SupervisionRelationshipId", "Permission", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 'ViewProgress', NOW() - INTERVAL '39 days', false),
    (2, 2, 'ViewResults', NOW() - INTERVAL '39 days', false),
    (3, 3, 'AssignActivity', NOW() - INTERVAL '37 days', false),
    (4, 4, 'ReceiveReport', NOW() - INTERVAL '37 days', false),
    (5, 5, 'ApproveReadingLevel', NOW() - INTERVAL '36 days', false),
    (6, 6, 'ApproveStory', NOW() - INTERVAL '34 days', false),
    (7, 7, 'ManageSafetySettings', NOW() - INTERVAL '34 days', false),
    (8, 8, 'ViewProgress', NOW() - INTERVAL '32 days', false),
    (9, 9, 'ViewResults', NOW() - INTERVAL '31 days', false),
    (10, 10, 'AssignActivity', NOW() - INTERVAL '17 days', false),
    (11, 1, 'GenerateStory', NOW() - INTERVAL '39 days', false),
    (12, 2, 'GenerateStory', NOW() - INTERVAL '39 days', false),
    (13, 3, 'GenerateStory', NOW() - INTERVAL '37 days', false),
    (14, 4, 'GenerateStory', NOW() - INTERVAL '37 days', false),
    (15, 5, 'GenerateStory', NOW() - INTERVAL '36 days', false),
    (16, 6, 'GenerateStory', NOW() - INTERVAL '34 days', false),
    (17, 7, 'GenerateStory', NOW() - INTERVAL '34 days', false),
    (18, 8, 'GenerateStory', NOW() - INTERVAL '32 days', false),
    (19, 10, 'GenerateStory', NOW() - INTERVAL '17 days', false),
    (20, 12, 'ViewProgress', NOW() - INTERVAL '2 days', false),
    (21, 12, 'ReceiveReport', NOW() - INTERVAL '2 days', false),
    (22, 15, 'ViewResults', NOW() - INTERVAL '6 hours', false),
    (23, 17, 'ViewResults', NOW() - INTERVAL '3 hours', false)
ON CONFLICT ("Id") DO NOTHING;
