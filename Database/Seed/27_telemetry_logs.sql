-- 27. telemetry_logs (19)
INSERT INTO telemetry_logs ("Id", "ReadingSessionId", "EventType", "EventPayload", "OccurredAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 'SessionCompleted', '{"pages":10}', NOW() - INTERVAL '10 days', NOW() - INTERVAL '10 days', false),
    (2, 2, 'SessionStarted', '{}', NOW() - INTERVAL '9 days', NOW() - INTERVAL '9 days', false),
    (3, 3, 'PageCompleted', '{"page":8}', NOW() - INTERVAL '8 days', NOW() - INTERVAL '8 days', false),
    (4, 4, 'PageViewed', '{"page":5}', NOW() - INTERVAL '7 days', NOW() - INTERVAL '7 days', false),
    (5, 5, 'QuizCompleted', '{"score":100}', NOW() - INTERVAL '6 days', NOW() - INTERVAL '6 days', false),
    (6, 6, 'TtsStarted', '{}', NOW() - INTERVAL '5 days', NOW() - INTERVAL '5 days', false),
    (7, 7, 'VocabularyOpened', '{"term":"sáng tạo"}', NOW() - INTERVAL '4 days', NOW() - INTERVAL '4 days', false),
    (8, 8, 'StoryFavorited', '{"storyId":8}', NOW() - INTERVAL '3 days', NOW() - INTERVAL '3 days', false),
    (9, 9, 'QuizAnswered', '{"correct":true}', NOW() - INTERVAL '2 days', NOW() - INTERVAL '2 days', false),
    (10, 10, 'BadgeUnlocked', '{"badge":"FAST_READER"}', NOW() - INTERVAL '1 days', NOW() - INTERVAL '1 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- Hoạt động của bé 1 ở phiên 101 / 102: đọc to (có 1 dòng payload hỏng để kiểm bỏ qua),
-- EXP nhận được và từ vựng đã lưu.
INSERT INTO telemetry_logs ("Id", "ReadingSessionId", "EventType", "EventPayload", "OccurredAt", "CreatedAt", "IsDeleted")
VALUES
    (101, 101, 'AsrWordResult', '{"segmentId":1,"total":10,"good":4,"retry":5,"skipped":1}', NOW() - INTERVAL '5 days' + INTERVAL '1 minute', NOW(), false),
    (102, 101, 'AsrWordResult', '{"segmentId":1,"total":10,"good":9,"retry":1,"skipped":0}', NOW() - INTERVAL '5 days' + INTERVAL '2 minutes', NOW(), false),
    (103, 101, 'AsrWordResult', '{"segmentId":2,"total":10,"good":7,"retry":2,"skipped":1}', NOW() - INTERVAL '5 days' + INTERVAL '3 minutes', NOW(), false),
    (104, 101, 'AsrWordResult', 'khong phai json', NOW() - INTERVAL '5 days' + INTERVAL '4 minutes', NOW(), false),
    (105, 101, 'RewardGranted', '{"exp":20,"reason":"StoryCompleted"}', NOW() - INTERVAL '5 days' + INTERVAL '5 minutes', NOW(), false),
    (106, 101, 'RewardGranted', '{"exp":5,"reason":"QuizAnswer"}', NOW() - INTERVAL '5 days' + INTERVAL '6 minutes', NOW(), false),
    (107, 101, 'VocabularyCollected', '{"vocabularyId":2}', NOW() - INTERVAL '5 days' + INTERVAL '7 minutes', NOW(), false),
    (108, 101, 'VocabularyCollected', '{"vocabularyId":1}', NOW() - INTERVAL '5 days' + INTERVAL '8 minutes', NOW(), false),
    (109, 102, 'RewardGranted', '{"exp":10,"reason":"StoryCompleted"}', NOW() - INTERVAL '30 hours', NOW(), false)
ON CONFLICT ("Id") DO NOTHING;
