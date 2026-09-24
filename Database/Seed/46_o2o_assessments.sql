-- 46. o2o_assessments (10)
INSERT INTO o2o_assessments ("Id", "AssignmentRecipientId", "TeacherUserId", "BonusPoints", "Notes", "AssessedAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 7, 10, 'Hoan thanh bai tap tot, can khich le them', NOW() - INTERVAL '5 days', NOW() - INTERVAL '5 days', false),
    (2, 2, 8, 5, 'Can co gang hon o phan doc hieu', NOW() - INTERVAL '4 days', NOW() - INTERVAL '4 days', false),
    (3, 3, 9, 15, 'Xuat sac, hoan thanh truoc thoi han', NOW() - INTERVAL '3 days', NOW() - INTERVAL '3 days', false),
    (4, 4, 10, 0, 'Chua nop bai dung han', NOW() - INTERVAL '2 days', NOW() - INTERVAL '2 days', false),
    (5, 5, 7, 8, 'Tien bo ro ret so voi lan truoc', NOW() - INTERVAL '1 days', NOW() - INTERVAL '1 days', false),
    (6, 6, 8, 12, 'Tra loi tot cac cau hoi thao luan', NOW() - INTERVAL '1 days', NOW() - INTERVAL '1 days', false),
    (7, 7, 9, 7, 'Hoan thanh dung han, chat luong on', NOW() - INTERVAL '1 days', NOW() - INTERVAL '1 days', false),
    (8, 8, 10, 9, 'Co su sang tao trong cau tra loi', NOW() - INTERVAL '1 days', NOW() - INTERVAL '1 days', false),
    (9, 9, 7, 6, 'Can luyen tap them ve tu vung', NOW() - INTERVAL '1 days', NOW() - INTERVAL '1 days', false),
    (10, 10, 8, 20, 'Xuat sac toan dien, danh dau la hoc sinh tieu bieu', NOW() - INTERVAL '1 days', NOW() - INTERVAL '1 days', false)
ON CONFLICT ("Id") DO NOTHING;
