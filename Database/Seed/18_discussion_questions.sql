-- 18. discussion_questions (16)
INSERT INTO discussion_questions ("Id", "StoryVersionId", "Question", "IsMoralLesson", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 'Bài học chính của câu chuyện là gì?', true, NOW() - INTERVAL '30 days', false),
    (2, 2, 'Vì sao chúng ta cần bảo vệ rừng xanh?', true, NOW() - INTERVAL '28 days', false),
    (3, 3, 'Nhân vật nào trong truyện thể hiện lòng tốt?', false, NOW() - INTERVAL '26 days', false),
    (4, 4, 'Con học được điều gì từ chuyến du hành vũ trụ?', true, NOW() - INTERVAL '24 days', false),
    (5, 5, 'Tình bạn trong câu chuyện được thể hiện như thế nào?', false, NOW() - INTERVAL '22 days', false),
    (6, 6, 'Làm sao để bảo vệ đại dương?', true, NOW() - INTERVAL '20 days', false),
    (7, 7, 'Gia đình trong truyện có điểm gì đặc biệt?', false, NOW() - INTERVAL '19 days', false),
    (8, 8, 'Vì sao siêu nhân tí hon lại dũng cảm?', true, NOW() - INTERVAL '17 days', false),
    (9, 9, 'Đoàn kết giúp nhóm bạn vượt qua khó khăn như thế nào?', true, NOW() - INTERVAL '15 days', false),
    (10, 10, 'Fair-play trong thể thao nghĩa là gì?', true, NOW() - INTERVAL '13 days', false),
    (11, 14, 'Vì sao Mai chăm sóc khu vườn?', true, NOW() - INTERVAL '7 days', false),
    (12, 15, 'Nam nên làm gì trước khi hái hoa?', true, NOW() - INTERVAL '2 days', false),
    (13, 16, 'Hai bạn đã chia sẻ khu vườn như thế nào?', true, NOW() - INTERVAL '2 hours', false),
    (14, 17, 'Ánh sáng đã giúp các bạn như thế nào?', true, NOW() - INTERVAL '8 days', false),
    (15, 18, 'Chi tiết nào cần sửa để an toàn hơn?', true, NOW() - INTERVAL '1 day', false),
    (16, 19, 'An đã giúp cả nhóm tìm đường về như thế nào?', true, NOW() - INTERVAL '1 hour', false)
ON CONFLICT ("Id") DO NOTHING;
