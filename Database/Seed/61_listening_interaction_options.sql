-- 61. listening_interaction_options (11, thuộc 5 tương tác của 60_listening_interactions.sql)
-- 900: thẻ cảm xúc (900 là thẻ khớp); 901: thẻ đoán tiếp (904 là thẻ khớp); 902 RolePlay không có thẻ;
-- 903: hai hình Đoán tranh (906 khớp, có mô tả để sinh ảnh); 904: ba thẻ sự kiện, CorrectOrder 1-3,
-- SourceStart/SourceEnd là vị trí câu nguyên văn của từng sự kiện trong nội dung truyện 900.
INSERT INTO listening_interaction_options (
    "Id", "ListeningInteractionId", "SortOrder", "Text", "Feedback", "IsMatch", "CorrectOrder", "SourceQuote", "SourceStart", "SourceEnd", "ImageDescription", "CreatedAt", "IsDeleted"
)
VALUES
    (900, 900, 1, 'Thỏ đang sợ', 'Ừ, trời tối Thỏ thấy sợ.', true, NULL, NULL, NULL, NULL, NULL, NOW() - INTERVAL '1 day', false),
    (901, 900, 2, 'Thỏ đang vui', 'Cũng có thể lắm!', false, NULL, NULL, NULL, NULL, NULL, NOW() - INTERVAL '1 day', false),
    (902, 900, 3, 'Thỏ đang buồn ngủ', 'Ý hay đó!', false, NULL, NULL, NULL, NULL, NULL, NOW() - INTERVAL '1 day', false),
    (903, 900, 4, 'Thỏ đang tò mò', 'Thỏ cũng tò mò lắm!', false, NULL, NULL, NULL, NULL, NULL, NOW() - INTERVAL '1 day', false),
    (904, 901, 1, 'Bác Gấu tốt bụng', 'Ý hay đó! Mình cùng nghe tiếp nhé.', true, NULL, NULL, NULL, NULL, NULL, NOW() - INTERVAL '1 day', false),
    (905, 901, 2, 'Một cơn gió mạnh', 'Cũng có thể lắm! Nghe tiếp xem sao nhé.', false, NULL, NULL, NULL, NULL, NULL, NOW() - INTERVAL '1 day', false),
    (906, 903, 1, 'Mẹ Thỏ', 'Đúng là mẹ Thỏ đang chờ đó!', true, NULL, NULL, NULL, NULL, 'Thỏ mẹ lông trắng đứng trước cửa hang', NOW() - INTERVAL '1 day', false),
    (907, 903, 2, 'Một bạn sóc nhỏ', 'Ý hay đó!', false, NULL, NULL, NULL, NULL, 'Sóc nhỏ đuôi xù trên cành cây thấp', NOW() - INTERVAL '1 day', false),
    (908, 904, 1, 'Thỏ đi lạc vào rừng', NULL, false, 1, 'Thỏ con đi lạc vào rừng.', 0, 24, NULL, NOW() - INTERVAL '1 day', false),
    (909, 904, 2, 'Bác Gấu đến giúp Thỏ', NULL, false, 2, 'Hóa ra đó là bác Gấu đến giúp Thỏ tìm đường về.', 111, 158, NULL, NOW() - INTERVAL '1 day', false),
    (910, 904, 3, 'Thỏ biết nhờ người lớn giúp', NULL, false, 3, 'Từ đó Thỏ luôn nhờ người lớn khi bị lạc.', 219, 259, NULL, NOW() - INTERVAL '1 day', false)
ON CONFLICT ("Id") DO NOTHING;
