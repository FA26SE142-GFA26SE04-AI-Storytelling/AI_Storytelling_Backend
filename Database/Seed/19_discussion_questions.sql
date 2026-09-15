-- 19. discussion_questions (10)
INSERT INTO discussion_questions ("Id", "StoryVersionId", "Question", "IsMoralLesson", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 'Bai hoc chinh cua cau chuyen la gi?', true, NOW() - INTERVAL '30 days', false),
    (2, 2, 'Vi sao chung ta can bao ve rung xanh?', true, NOW() - INTERVAL '28 days', false),
    (3, 3, 'Nhan vat nao trong truyen the hien long tot?', false, NOW() - INTERVAL '26 days', false),
    (4, 4, 'Con hoc duoc dieu gi tu chuyen du hanh vu tru?', true, NOW() - INTERVAL '24 days', false),
    (5, 5, 'Tinh ban trong cau chuyen duoc the hien nhu the nao?', false, NOW() - INTERVAL '22 days', false),
    (6, 6, 'Lam sao de bao ve dai duong?', true, NOW() - INTERVAL '20 days', false),
    (7, 7, 'Gia dinh trong truyen co diem gi dac biet?', false, NOW() - INTERVAL '19 days', false),
    (8, 8, 'Vi sao sieu nhan ti hon lai dung cam?', true, NOW() - INTERVAL '17 days', false),
    (9, 9, 'Doan ket giup nhom ban vuot qua kho khan nhu the nao?', true, NOW() - INTERVAL '15 days', false),
    (10, 10, 'Fair-play trong the thao nghia la gi?', true, NOW() - INTERVAL '13 days', false)
ON CONFLICT ("Id") DO NOTHING;
