-- 61. listening_interaction_options (54: bộ cũ 900-912, bộ tuổi 6 920-928, bộ tuổi 7 930-941, bộ tuổi 8 942-961)
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
    (909, 904, 2, 'Bác Gấu đến giúp Thỏ', NULL, false, 2, 'Hóa ra đó là bác Gấu đến giúp Thỏ tìm đường về.', 148, 195, NULL, NOW() - INTERVAL '1 day', false),
    (910, 904, 3, 'Thỏ biết nhờ người lớn giúp', NULL, false, 3, 'Từ đó Thỏ luôn nhờ người lớn khi bị lạc.', 256, 296, NULL, NOW() - INTERVAL '1 day', false)
ON CONFLICT ("Id") DO NOTHING;

-- Thẻ bộ tuổi 7 của truyện 900: đúng/sai, ghép câu, kéo thả và xếp sự kiện.
INSERT INTO listening_interaction_options (
    "Id", "ListeningInteractionId", "SortOrder", "Text", "Feedback", "IsMatch", "Role", "CorrectOrder",
    "SourceQuote", "SourceStart", "SourceEnd", "ImageDescription", "CreatedAt", "IsDeleted"
)
VALUES
    (930, 931, 1, 'Thỏ nghe tiếng sột soạt sau bụi cây', NULL, true, 'None', NULL, NULL, NULL, NULL, NULL, NOW() - INTERVAL '1 day', false),
    (931, 931, 2, 'Thỏ nghe tiếng chuông trên cây', NULL, false, 'None', NULL, NULL, NULL, NULL, NULL, NOW() - INTERVAL '1 day', false),
    (932, 932, 1, 'Thỏ con', NULL, false, 'None', 1, NULL, NULL, NULL, NULL, NOW() - INTERVAL '1 day', false),
    (933, 932, 2, 'đi lạc', NULL, false, 'None', 2, NULL, NULL, NULL, NULL, NOW() - INTERVAL '1 day', false),
    (934, 932, 3, 'vào', NULL, false, 'None', 3, NULL, NULL, NULL, NULL, NOW() - INTERVAL '1 day', false),
    (935, 932, 4, 'rừng', NULL, false, 'None', 4, NULL, NULL, NULL, NULL, NOW() - INTERVAL '1 day', false),
    (936, 933, 1, 'bác Gấu', NULL, true, 'Item', NULL, NULL, NULL, NULL, 'Bác Gấu nâu đứng trong rừng', NOW() - INTERVAL '1 day', false),
    (937, 933, 2, 'bụi cây', NULL, false, 'Item', NULL, NULL, NULL, NULL, 'Bụi cây xanh trong rừng', NOW() - INTERVAL '1 day', false),
    (938, 933, 3, 'cửa hang', NULL, false, 'Target', NULL, NULL, NULL, NULL, 'Cửa hang trong rừng', NOW() - INTERVAL '1 day', false),
    (939, 934, 1, 'Thỏ đi lạc vào rừng', NULL, false, 'None', 1, 'Thỏ con đi lạc vào rừng.', 0, 24, NULL, NOW() - INTERVAL '1 day', false),
    (940, 934, 2, 'Bác Gấu đến giúp Thỏ', NULL, false, 'None', 2, 'Hóa ra đó là bác Gấu đến giúp Thỏ tìm đường về.', 148, 195, NULL, NOW() - INTERVAL '1 day', false),
    (941, 934, 3, 'Bác Gấu dẫn Thỏ về cửa hang', NULL, false, 'None', 3, 'Bác Gấu dẫn Thỏ về tới cửa hang.', 197, 229, NULL, NOW() - INTERVAL '1 day', false)
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

-- Thẻ bộ tuổi 8 của truyện 900.
-- 935: ba đáp án (942 đúng) + ba manh mối (Role Clue, nguyên văn trong đoạn); 936: hai nhân vật Thỏ và Bác Gấu, CorrectOrder = thứ tự lượt;
-- 937: ba tiêu đề (950 bao quát cả cảnh, Feedback nói vì sao); 938: ba bong bóng (953 đúng) kèm cảm xúc;
-- 939: ba thẻ kết quả (Item 956-958) và ba thẻ lí do (Target 959-961), cặp đúng cùng CorrectOrder, mỗi thẻ có câu trích.
INSERT INTO listening_interaction_options (
    "Id", "ListeningInteractionId", "SortOrder", "Text", "Feedback", "IsMatch", "Role", "CorrectOrder",
    "SourceQuote", "SourceStart", "SourceEnd", "Speaker", "Emotion", "CreatedAt", "IsDeleted"
)
VALUES
    (942, 935, 1, 'Vì Thỏ lạc đường lúc trời tối', NULL, true, 'None', NULL, NULL, NULL, NULL, NULL, NULL, NOW() - INTERVAL '1 day', false),
    (943, 935, 2, 'Vì Thỏ muốn đi ngủ', NULL, false, 'None', NULL, NULL, NULL, NULL, NULL, NULL, NOW() - INTERVAL '1 day', false),
    (944, 935, 3, 'Vì Thỏ đang chơi trốn tìm', NULL, false, 'None', NULL, NULL, NULL, NULL, NULL, NULL, NOW() - INTERVAL '1 day', false),
    (945, 935, 4, 'Thỏ con', NULL, false, 'Clue', NULL, NULL, NULL, NULL, NULL, NULL, NOW() - INTERVAL '1 day', false),
    (946, 935, 5, 'đi lạc vào rừng', NULL, false, 'Clue', NULL, NULL, NULL, NULL, NULL, NULL, NOW() - INTERVAL '1 day', false),
    (947, 935, 6, 'Trời bắt đầu tối', NULL, false, 'Clue', NULL, NULL, NULL, NULL, NULL, NULL, NOW() - INTERVAL '1 day', false),
    (948, 936, 1, 'Có ai ở đó không?', NULL, false, 'None', 1, NULL, NULL, NULL, 'Thỏ', NULL, NOW() - INTERVAL '1 day', false),
    (949, 936, 2, 'Bác đây, đừng sợ nhé!', NULL, false, 'None', 2, NULL, NULL, NULL, 'Bác Gấu', NULL, NOW() - INTERVAL '1 day', false),
    (950, 937, 1, 'Bác Gấu giúp Thỏ tìm đường', 'Tiêu đề này nói về cả cảnh.', true, 'None', NULL, NULL, NULL, NULL, NULL, NULL, NOW() - INTERVAL '1 day', false),
    (951, 937, 2, 'Tiếng sột soạt sau bụi cây', 'Tiêu đề này chỉ nói về một chi tiết.', false, 'None', NULL, NULL, NULL, NULL, NULL, NULL, NOW() - INTERVAL '1 day', false),
    (952, 937, 3, 'Trời bắt đầu tối', 'Tiêu đề này chỉ nói về một chi tiết.', false, 'None', NULL, NULL, NULL, NULL, NULL, NULL, NOW() - INTERVAL '1 day', false),
    (953, 938, 1, 'Mình an toàn rồi, vui quá!', NULL, true, 'None', NULL, NULL, NULL, NULL, NULL, 'vui', NOW() - INTERVAL '1 day', false),
    (954, 938, 2, 'Mình còn sợ lắm.', NULL, false, 'None', NULL, NULL, NULL, NULL, NULL, 'sợ', NOW() - INTERVAL '1 day', false),
    (955, 938, 3, 'Mình muốn đi tiếp.', NULL, false, 'None', NULL, NULL, NULL, NULL, NULL, 'háo hức', NOW() - INTERVAL '1 day', false),
    (956, 939, 1, 'Thỏ hỏi có ai ở đó không', NULL, false, 'Item', 1, 'Thỏ hỏi: "Có ai ở đó không?"', 81, 109, NULL, NULL, NOW() - INTERVAL '1 day', false),
    (957, 939, 2, 'Bác Gấu giúp Thỏ', NULL, false, 'Item', 2, 'Hóa ra đó là bác Gấu đến giúp Thỏ tìm đường về.', 148, 195, NULL, NULL, NOW() - INTERVAL '1 day', false),
    (958, 939, 3, 'Thỏ về tới cửa hang', NULL, false, 'Item', 3, 'Bác Gấu dẫn Thỏ về tới cửa hang.', 197, 229, NULL, NULL, NOW() - INTERVAL '1 day', false),
    (959, 939, 4, 'Thỏ nghe tiếng sột soạt', NULL, false, 'Target', 1, 'Thỏ nghe tiếng sột soạt sau bụi cây.', 44, 80, NULL, NULL, NOW() - INTERVAL '1 day', false),
    (960, 939, 5, 'Thỏ bị lạc đường', NULL, false, 'Target', 2, 'Thỏ con đi lạc vào rừng.', 0, 24, NULL, NULL, NOW() - INTERVAL '1 day', false),
    (961, 939, 6, 'Bác Gấu dẫn đường', NULL, false, 'Target', 3, 'Hóa ra đó là bác Gấu đến giúp Thỏ', 148, 181, NULL, NULL, NOW() - INTERVAL '1 day', false)
ON CONFLICT ("Id") DO NOTHING;
