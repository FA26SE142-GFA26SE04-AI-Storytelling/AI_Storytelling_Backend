-- 38. vocabulary_notebook_entries (10)
INSERT INTO vocabulary_notebook_entries ("Id", "ChildProfileId", "StoryVocabularyId", "CollectedAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 1, NOW() - INTERVAL '10 days', NOW() - INTERVAL '10 days', false),
    (2, 2, 2, NOW() - INTERVAL '9 days', NOW() - INTERVAL '9 days', false),
    (3, 3, 3, NOW() - INTERVAL '8 days', NOW() - INTERVAL '8 days', false),
    (4, 4, 4, NOW() - INTERVAL '7 days', NOW() - INTERVAL '7 days', false),
    (5, 5, 5, NOW() - INTERVAL '6 days', NOW() - INTERVAL '6 days', false),
    (6, 6, 6, NOW() - INTERVAL '5 days', NOW() - INTERVAL '5 days', false),
    (7, 7, 7, NOW() - INTERVAL '4 days', NOW() - INTERVAL '4 days', false),
    (8, 8, 8, NOW() - INTERVAL '3 days', NOW() - INTERVAL '3 days', false),
    (9, 9, 9, NOW() - INTERVAL '2 days', NOW() - INTERVAL '2 days', false),
    (10, 10, 10, NOW() - INTERVAL '1 days', NOW() - INTERVAL '1 days', false)
ON CONFLICT ("Id") DO NOTHING;
