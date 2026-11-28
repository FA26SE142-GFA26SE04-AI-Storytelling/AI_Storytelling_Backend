-- 61. listening_interaction_options (22: 900-910 của 5 tương tác bộ cũ truyện 900, 911-912 của tương tác 905, 920-928 của bộ 6 tuổi)
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

-- 905 FillBlank: mỗi option là một đáp án chấp nhận (không có Feedback / IsMatch, không bao giờ trả cho bé).
-- So khớp không phân biệt hoa thường và khoảng trắng thừa nhưng có phân biệt dấu ("khung long" không khớp).
INSERT INTO listening_interaction_options (
    "Id", "ListeningInteractionId", "SortOrder", "Text", "Feedback", "IsMatch", "CorrectOrder", "SourceQuote", "SourceStart", "SourceEnd", "ImageDescription", "CreatedAt", "IsDeleted"
)
VALUES
    (911, 905, 1, 'khủng long', NULL, false, NULL, NULL, NULL, NULL, NULL, NOW() - INTERVAL '2 days', false),
    (912, 905, 2, 'con khủng long', NULL, false, NULL, NULL, NULL, NULL, NULL, NOW() - INTERVAL '2 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- Bộ tuổi 6 của truyện 900 (interactions 920-924). Mô tả hình do AI soạn từ chính cảnh truyện, không dùng ảnh ngoài.
-- 920 Literal: mỗi đáp án là tranh kèm chữ (920 khớp). 922 WhoSaidIt: 922 Thỏ khớp. 923 CharacterLetter:
-- chữ khuyết là "th" (ô đầu của "Thỏ"), 924 khớp có hình chân dung. 924 PicturePrediction tuổi 6: hai hình, không có thẻ khớp.
INSERT INTO listening_interaction_options (
    "Id", "ListeningInteractionId", "SortOrder", "Text", "Feedback", "IsMatch", "CorrectOrder", "SourceQuote", "SourceStart", "SourceEnd", "ImageDescription", "CreatedAt", "IsDeleted"
)
VALUES
    (920, 920, 1, 'Thỏ con', 'Đúng là Thỏ con đi lạc!', true, NULL, NULL, NULL, NULL, 'Thỏ con lông trắng đứng một mình giữa khu rừng lúc chiều tối', NOW() - INTERVAL '1 day', false),
    (921, 920, 2, 'bác Gấu', 'Mình cùng xem lại nhé.', false, NULL, NULL, NULL, NULL, 'Bác Gấu nâu to đứng cạnh bụi cây trong rừng', NOW() - INTERVAL '1 day', false),
    (922, 922, 1, 'Thỏ', NULL, true, NULL, NULL, NULL, NULL, 'Thỏ con lông trắng nhìn quanh trong rừng tối', NOW() - INTERVAL '1 day', false),
    (923, 922, 2, 'bác Gấu', NULL, false, NULL, NULL, NULL, NULL, 'Bác Gấu nâu to núp sau bụi cây', NOW() - INTERVAL '1 day', false),
    (924, 923, 1, 'th', NULL, true, NULL, NULL, NULL, NULL, 'Chân dung Thỏ con lông trắng, tai dài, nhìn thẳng', NOW() - INTERVAL '1 day', false),
    (925, 923, 2, 'b', NULL, false, NULL, NULL, NULL, NULL, NULL, NOW() - INTERVAL '1 day', false),
    (926, 923, 3, 'm', NULL, false, NULL, NULL, NULL, NULL, NULL, NOW() - INTERVAL '1 day', false),
    (927, 924, 1, 'Thỏ chạy vào hang', 'Ý hay quá!', false, NULL, NULL, NULL, NULL, 'Thỏ con chạy vào cửa hang ấm áp', NOW() - INTERVAL '1 day', false),
    (928, 924, 2, 'Thỏ chào bác Gấu', 'Ý hay quá!', false, NULL, NULL, NULL, NULL, 'Thỏ con vẫy tay chào bác Gấu trước cửa hang', NOW() - INTERVAL '1 day', false)
ON CONFLICT ("Id") DO NOTHING;
