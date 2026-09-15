-- 15. class_group_members (10)
INSERT INTO class_group_members ("Id", "ClassGroupId", "ChildProfileId", "JoinedAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 2, NOW() - INTERVAL '35 days', NOW() - INTERVAL '35 days', false),
    (2, 1, 6, NOW() - INTERVAL '34 days', NOW() - INTERVAL '34 days', false),
    (3, 2, 4, NOW() - INTERVAL '33 days', NOW() - INTERVAL '33 days', false),
    (4, 2, 8, NOW() - INTERVAL '32 days', NOW() - INTERVAL '32 days', false),
    (5, 3, 10, NOW() - INTERVAL '17 days', NOW() - INTERVAL '17 days', false),
    (6, 3, 2, NOW() - INTERVAL '31 days', NOW() - INTERVAL '31 days', false),
    (7, 4, 6, NOW() - INTERVAL '30 days', NOW() - INTERVAL '30 days', false),
    (8, 4, 10, NOW() - INTERVAL '16 days', NOW() - INTERVAL '16 days', false),
    (9, 5, 4, NOW() - INTERVAL '15 days', NOW() - INTERVAL '15 days', false),
    (10, 5, 8, NOW() - INTERVAL '14 days', NOW() - INTERVAL '14 days', false)
ON CONFLICT ("Id") DO NOTHING;
