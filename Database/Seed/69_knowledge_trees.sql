-- 69. knowledge_trees (6 - unique theo ChildProfileId; ExpInvested <= achievements.ExpTotal o 29_achievements)
-- Be 5, 6, 7, 9 khong co dong: chua tung tuoi cay, dung de test tao cay lan dau.
INSERT INTO knowledge_trees ("Id", "ChildProfileId", "ExpInvested", "CurrentTreeExp", "FloweredTreeCount", "LastWateredAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 100, 100, 0, NOW() - INTERVAL '2 days', NOW() - INTERVAL '10 days', false),
    (2, 2, 40, 40, 0, NOW() - INTERVAL '3 days', NOW() - INTERVAL '9 days', false),
    (3, 3, 200, 200, 0, NOW() - INTERVAL '1 days', NOW() - INTERVAL '8 days', false),
    (4, 4, 150, 150, 0, NOW() - INTERVAL '1 days', NOW() - INTERVAL '7 days', false),
    (5, 8, 250, 250, 0, NOW() - INTERVAL '1 days', NOW() - INTERVAL '3 days', false),
    (6, 10, 0, 0, 0, NULL, NOW() - INTERVAL '1 days', false)
ON CONFLICT ("Id") DO NOTHING;
