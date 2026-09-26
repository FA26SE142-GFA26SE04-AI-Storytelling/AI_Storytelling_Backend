-- 40. badges (10)
INSERT INTO badges ("Id", "ChildProfileId", "BadgeCode", "EarnedAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 'FIRST_STORY_COMPLETED', NOW() - INTERVAL '10 days', NOW() - INTERVAL '10 days', false),
    (2, 2, 'FAST_READER', NOW() - INTERVAL '9 days', NOW() - INTERVAL '9 days', false),
    (3, 3, 'VOCAB_MASTER', NOW() - INTERVAL '8 days', NOW() - INTERVAL '8 days', false),
    (4, 4, 'QUIZ_CHAMPION', NOW() - INTERVAL '7 days', NOW() - INTERVAL '7 days', false),
    (5, 5, 'STREAK_7_DAYS', NOW() - INTERVAL '6 days', NOW() - INTERVAL '6 days', false),
    (6, 6, 'BOOKWORM', NOW() - INTERVAL '5 days', NOW() - INTERVAL '5 days', false),
    (7, 7, 'EARLY_BIRD', NOW() - INTERVAL '4 days', NOW() - INTERVAL '4 days', false),
    (8, 8, 'NIGHT_OWL', NOW() - INTERVAL '3 days', NOW() - INTERVAL '3 days', false),
    (9, 9, 'HELPER', NOW() - INTERVAL '2 days', NOW() - INTERVAL '2 days', false),
    (10, 10, 'EXPLORER', NOW() - INTERVAL '1 days', NOW() - INTERVAL '1 days', false)
ON CONFLICT ("Id") DO NOTHING;
