-- 37. telemetry_logs (10)
INSERT INTO telemetry_logs ("Id", "ReadingSessionId", "EventType", "EventPayload", "OccurredAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 'SessionCompleted', '{"pages":10}', NOW() - INTERVAL '10 days', NOW() - INTERVAL '10 days', false),
    (2, 2, 'SessionStarted', '{}', NOW() - INTERVAL '9 days', NOW() - INTERVAL '9 days', false),
    (3, 3, 'PageCompleted', '{"page":8}', NOW() - INTERVAL '8 days', NOW() - INTERVAL '8 days', false),
    (4, 4, 'PageViewed', '{"page":5}', NOW() - INTERVAL '7 days', NOW() - INTERVAL '7 days', false),
    (5, 5, 'QuizCompleted', '{"score":100}', NOW() - INTERVAL '6 days', NOW() - INTERVAL '6 days', false),
    (6, 6, 'TtsStarted', '{}', NOW() - INTERVAL '5 days', NOW() - INTERVAL '5 days', false),
    (7, 7, 'VocabularyOpened', '{"term":"sang tao"}', NOW() - INTERVAL '4 days', NOW() - INTERVAL '4 days', false),
    (8, 8, 'StoryFavorited', '{"storyId":8}', NOW() - INTERVAL '3 days', NOW() - INTERVAL '3 days', false),
    (9, 9, 'QuizAnswered', '{"correct":true}', NOW() - INTERVAL '2 days', NOW() - INTERVAL '2 days', false),
    (10, 10, 'BadgeUnlocked', '{"badge":"FAST_READER"}', NOW() - INTERVAL '1 days', NOW() - INTERVAL '1 days', false)
ON CONFLICT ("Id") DO NOTHING;
