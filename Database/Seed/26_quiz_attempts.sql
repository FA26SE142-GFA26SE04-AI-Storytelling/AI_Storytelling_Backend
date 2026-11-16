-- 26. quiz_attempts (14)
INSERT INTO quiz_attempts ("Id", "QuizItemId", "ReadingSessionId", "AnswerGiven", "IsCorrect", "AnsweredAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 1, 'Chú Thỏ', true, NOW() - INTERVAL '10 days', NOW() - INTERVAL '10 days', false),
    (2, 2, 2, 'Dung', true, NOW() - INTERVAL '9 days', NOW() - INTERVAL '9 days', false),
    (3, 3, 3, 'Công nghệ', false, NOW() - INTERVAL '8 days', NOW() - INTERVAL '8 days', false),
    (4, 4, 4, 'Sao Hỏa', true, NOW() - INTERVAL '7 days', NOW() - INTERVAL '7 days', false),
    (5, 5, 5, 'Dung', true, NOW() - INTERVAL '6 days', NOW() - INTERVAL '6 days', false),
    (6, 6, 6, 'Kho báu', false, NOW() - INTERVAL '5 days', NOW() - INTERVAL '5 days', false),
    (7, 7, 7, 'Quây quần bên nhau', true, NOW() - INTERVAL '4 days', NOW() - INTERVAL '4 days', false),
    (8, 8, 8, 'Dung', true, NOW() - INTERVAL '3 days', NOW() - INTERVAL '3 days', false),
    (9, 9, 9, 'Bon', true, NOW() - INTERVAL '2 days', NOW() - INTERVAL '2 days', false),
    (10, 10, 10, 'Chơi đẹp và trung thực', true, NOW() - INTERVAL '1 days', NOW() - INTERVAL '1 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- Bé 1 làm quiz phiên bản 1 (câu 1 và 101) ở phiên 101 và 102. Cùng attempt 1 (phiên 1),
-- lịch sử tách thành 3 lần làm: 50% -> 50% -> 100%.
INSERT INTO quiz_attempts ("Id", "QuizItemId", "ReadingSessionId", "AnswerGiven", "IsCorrect", "AnsweredAt", "CreatedAt", "IsDeleted")
VALUES
    (101, 1, 101, 'Chú Rùa', false, NOW() - INTERVAL '5 days', NOW() - INTERVAL '5 days', false),
    (102, 101, 101, 'Dung', true, NOW() - INTERVAL '5 days' + INTERVAL '1 minute', NOW() - INTERVAL '5 days', false),
    (103, 1, 102, 'Chú Thỏ', true, NOW() - INTERVAL '30 hours', NOW() - INTERVAL '30 hours', false),
    (104, 101, 102, 'Dung', true, NOW() - INTERVAL '30 hours' + INTERVAL '1 minute', NOW() - INTERVAL '30 hours', false)
ON CONFLICT ("Id") DO NOTHING;
