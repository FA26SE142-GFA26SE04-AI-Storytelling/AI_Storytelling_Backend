-- 20. story_vocabulary (16)
INSERT INTO story_vocabulary ("Id", "StoryVersionId", "Term", "Definition", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 'thông minh', 'Có khả năng suy nghĩ nhanh và chính xác', NOW() - INTERVAL '30 days', false),
    (2, 2, 'dũng cảm', 'Không sợ hãi trước khó khăn, nguy hiểm', NOW() - INTERVAL '28 days', false),
    (3, 3, 'kiên nhẫn', 'Chịu đựng, không nổi nóng khi gặp khó khăn', NOW() - INTERVAL '26 days', false),
    (4, 4, 'trung thực', 'Luôn nói thật, không gian dối', NOW() - INTERVAL '24 days', false),
    (5, 5, 'đoàn kết', 'Cùng nhau hợp sức để đạt mục tiêu chung', NOW() - INTERVAL '22 days', false),
    (6, 6, 'yêu thương', 'Tình cảm quan tâm, che chở người khác', NOW() - INTERVAL '20 days', false),
    (7, 7, 'sáng tạo', 'Có khả năng nghĩ ra ý tưởng mới', NOW() - INTERVAL '19 days', false),
    (8, 8, 'chăm chỉ', 'Chịu khó làm việc, không lười biếng', NOW() - INTERVAL '17 days', false),
    (9, 9, 'lễ phép', 'Cư xử đúng mực, tôn trọng người khác', NOW() - INTERVAL '15 days', false),
    (10, 10, 'tự tin', 'Tin tưởng vào khả năng của bản thân', NOW() - INTERVAL '13 days', false),
    (11, 14, 'chăm chỉ', 'Kiên trì làm một việc tốt mỗi ngày', NOW() - INTERVAL '7 days', false),
    (12, 15, 'xin phép', 'Hỏi ý kiến trước khi sử dụng đồ của người khác', NOW() - INTERVAL '2 days', false),
    (13, 16, 'sẻ chia', 'Cùng trao cho nhau niềm vui và điều tốt đẹp', NOW() - INTERVAL '2 hours', false),
    (14, 17, 'dẫn đường', 'Giúp người khác nhận ra lối đi', NOW() - INTERVAL '8 days', false),
    (15, 18, 'an toàn', 'Tránh những điều có thể gây nguy hiểm', NOW() - INTERVAL '1 day', false),
    (16, 19, 'dịu', 'Nhẹ nhàng, không gây chói hoặc khó chịu', NOW() - INTERVAL '1 hour', false)
ON CONFLICT ("Id") DO NOTHING;
