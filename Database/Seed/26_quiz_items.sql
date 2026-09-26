-- 26. quiz_items (10)
INSERT INTO quiz_items ("Id", "StoryVersionId", "Type", "Question", "Choices", "CorrectAnswer", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 'MultipleChoice', 'Nhan vat chinh trong cau chuyen la ai?', '["Chu Tho","Chu Ruoi","Chu Rua"]', 'Chu Tho', NOW() - INTERVAL '30 days', false),
    (2, 2, 'TrueFalse', 'Khu rung xanh co nhieu dieu ky dieu.', '["Dung","Sai"]', 'Dung', NOW() - INTERVAL '28 days', false),
    (3, 3, 'MultipleChoice', 'Vuong quoc trong truyen co dieu gi dac biet?', '["Phep thuat","Cong nghe","Chien tranh"]', 'Phep thuat', NOW() - INTERVAL '26 days', false),
    (4, 4, 'ShortAnswer', 'Ke ten mot hanh tinh xuat hien trong truyen.', NULL, 'Sao Hoa', NOW() - INTERVAL '24 days', false),
    (5, 5, 'TrueFalse', 'Hai nguoi ban trong truyen luon giup do nhau.', '["Dung","Sai"]', 'Dung', NOW() - INTERVAL '22 days', false),
    (6, 6, 'MultipleChoice', 'Bi mat duoi day dai duong la gi?', '["Rap san ho ky dieu","Kho bau","Tau dam"]', 'Rap san ho ky dieu', NOW() - INTERVAL '20 days', false),
    (7, 7, 'ShortAnswer', 'Gia dinh trong truyen thuong lam gi vao buoi toi?', NULL, 'Quay quan ben nhau', NOW() - INTERVAL '19 days', false),
    (8, 8, 'TrueFalse', 'Sieu nhan ti hon co suc manh dac biet.', '["Dung","Sai"]', 'Dung', NOW() - INTERVAL '17 days', false),
    (9, 9, 'MultipleChoice', 'Nhom ban trong truyen co bao nhieu nguoi?', '["Ba","Bon","Nam"]', 'Bon', NOW() - INTERVAL '15 days', false),
    (10, 10, 'ShortAnswer', 'Fair-play nghia la gi?', NULL, 'Choi dep va trung thuc', NOW() - INTERVAL '13 days', false)
ON CONFLICT ("Id") DO NOTHING;
