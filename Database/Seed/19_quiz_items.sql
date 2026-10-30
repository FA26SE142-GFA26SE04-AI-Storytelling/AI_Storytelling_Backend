-- 19. quiz_items (16)
INSERT INTO quiz_items ("Id", "StoryVersionId", "Type", "Question", "Choices", "CorrectAnswer", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 'MultipleChoice', 'Nhân vật chính trong câu chuyện là ai?', '["Chú Thỏ","Chú Ruồi","Chú Rùa"]', 'Chú Thỏ', NOW() - INTERVAL '30 days', false),
    (2, 2, 'TrueFalse', 'Khu rừng xanh có nhiều điều kỳ diệu.', '["Dung","Sai"]', 'Dung', NOW() - INTERVAL '28 days', false),
    (3, 3, 'MultipleChoice', 'Vương quốc trong truyện có điều gì đặc biệt?', '["Phép thuật","Công nghệ","Chiến tranh"]', 'Phép thuật', NOW() - INTERVAL '26 days', false),
    (4, 4, 'ShortAnswer', 'Kể tên một hành tinh xuất hiện trong truyện.', NULL, 'Sao Hỏa', NOW() - INTERVAL '24 days', false),
    (5, 5, 'TrueFalse', 'Hai người bạn trong truyện luôn giúp đỡ nhau.', '["Dung","Sai"]', 'Dung', NOW() - INTERVAL '22 days', false),
    (6, 6, 'MultipleChoice', 'Bí mật dưới đáy đại dương là gì?', '["Rạn san hô kỳ diệu","Kho báu","Tau dam"]', 'Rạn san hô kỳ diệu', NOW() - INTERVAL '20 days', false),
    (7, 7, 'ShortAnswer', 'Gia đình trong truyện thường làm gì vào buổi tối?', NULL, 'Quây quần bên nhau', NOW() - INTERVAL '19 days', false),
    (8, 8, 'TrueFalse', 'Siêu nhân tí hon có sức mạnh đặc biệt.', '["Dung","Sai"]', 'Dung', NOW() - INTERVAL '17 days', false),
    (9, 9, 'MultipleChoice', 'Nhóm bạn trong truyện có bao nhiêu người?', '["Ba","Bon","Nam"]', 'Bon', NOW() - INTERVAL '15 days', false),
    (10, 10, 'ShortAnswer', 'Fair-play nghĩa là gì?', NULL, 'Chơi đẹp và trung thực', NOW() - INTERVAL '13 days', false),
    (11, 14, 'MultipleChoice', 'Ai chăm sóc khu vườn?', '["Mai","Nam","Cả hai"]', 'Mai', NOW() - INTERVAL '7 days', false),
    (12, 15, 'MultipleChoice', 'Ai chăm sóc khu vườn?', '["Mai","Nam","Cả hai"]', 'Mai', NOW() - INTERVAL '2 days', false),
    (13, 16, 'MultipleChoice', 'Ai chăm sóc khu vườn?', '["Mai","Nam","Cả hai"]', 'Cả hai', NOW() - INTERVAL '2 hours', false),
    (14, 17, 'TrueFalse', 'Ngọn đèn giúp các bạn tìm đường.', '["Đúng","Sai"]', 'Đúng', NOW() - INTERVAL '8 days', false),
    (15, 18, 'TrueFalse', 'Đặt đèn sát rèm cửa là an toàn.', '["Đúng","Sai"]', 'Sai', NOW() - INTERVAL '1 day', false),
    (16, 19, 'TrueFalse', 'An đặt ngọn đèn ở vị trí an toàn.', '["Đúng","Sai"]', 'Đúng', NOW() - INTERVAL '1 hour', false)
ON CONFLICT ("Id") DO NOTHING;
