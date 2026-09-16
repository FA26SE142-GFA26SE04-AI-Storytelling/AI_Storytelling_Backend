-- 30. quiz_attempts (10)
INSERT INTO quiz_attempts ("Id", "QuizItemId", "ReadingSessionId", "AnswerGiven", "IsCorrect", "AnsweredAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 1, 'Chu Tho', true, NOW() - INTERVAL '10 days', NOW() - INTERVAL '10 days', false),
    (2, 2, 2, 'Dung', true, NOW() - INTERVAL '9 days', NOW() - INTERVAL '9 days', false),
    (3, 3, 3, 'Cong nghe', false, NOW() - INTERVAL '8 days', NOW() - INTERVAL '8 days', false),
    (4, 4, 4, 'Sao Hoa', true, NOW() - INTERVAL '7 days', NOW() - INTERVAL '7 days', false),
    (5, 5, 5, 'Dung', true, NOW() - INTERVAL '6 days', NOW() - INTERVAL '6 days', false),
    (6, 6, 6, 'Kho bau', false, NOW() - INTERVAL '5 days', NOW() - INTERVAL '5 days', false),
    (7, 7, 7, 'Quay quan ben nhau', true, NOW() - INTERVAL '4 days', NOW() - INTERVAL '4 days', false),
    (8, 8, 8, 'Dung', true, NOW() - INTERVAL '3 days', NOW() - INTERVAL '3 days', false),
    (9, 9, 9, 'Bon', true, NOW() - INTERVAL '2 days', NOW() - INTERVAL '2 days', false),
    (10, 10, 10, 'Choi dep va trung thuc', true, NOW() - INTERVAL '1 days', NOW() - INTERVAL '1 days', false)
ON CONFLICT ("Id") DO NOTHING;
