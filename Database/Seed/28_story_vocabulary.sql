-- 28. story_vocabulary (10)
INSERT INTO story_vocabulary ("Id", "StoryVersionId", "Term", "Definition", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 'thong minh', 'Co kha nang suy nghi nhanh va chinh xac', NOW() - INTERVAL '30 days', false),
    (2, 2, 'dung cam', 'Khong so hai truoc kho khan, nguy hiem', NOW() - INTERVAL '28 days', false),
    (3, 3, 'kien nhan', 'Chiu dung, khong noi nong khi gap kho khan', NOW() - INTERVAL '26 days', false),
    (4, 4, 'trung thuc', 'Luon noi that, khong gian doi', NOW() - INTERVAL '24 days', false),
    (5, 5, 'doan ket', 'Cung nhau hop suc de dat muc tieu chung', NOW() - INTERVAL '22 days', false),
    (6, 6, 'yeu thuong', 'Tinh cam quan tam, che cho nguoi khac', NOW() - INTERVAL '20 days', false),
    (7, 7, 'sang tao', 'Co kha nang nghi ra y tuong moi', NOW() - INTERVAL '19 days', false),
    (8, 8, 'cham chi', 'Chiu kho lam viec, khong luoi bieng', NOW() - INTERVAL '17 days', false),
    (9, 9, 'le phep', 'Cu xu dung muc, ton trong nguoi khac', NOW() - INTERVAL '15 days', false),
    (10, 10, 'tu tin', 'Tin tuong vao kha nang cua ban than', NOW() - INTERVAL '13 days', false)
ON CONFLICT ("Id") DO NOTHING;
