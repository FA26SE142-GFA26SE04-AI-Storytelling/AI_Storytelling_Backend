-- 15. learning_profiles (10 - unique theo ChildProfileId)
INSERT INTO learning_profiles ("Id", "ChildProfileId", "ReadingLevel", "ComprehensionGoal", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 2, 'Hieu duoc y chinh cua cau chuyen', NOW() - INTERVAL '40 days', false),
    (2, 2, 3, 'Tom tat duoc dien bien cau chuyen', NOW() - INTERVAL '39 days', false),
    (3, 3, 1, 'Nhan biet nhan vat chinh', NOW() - INTERVAL '38 days', false),
    (4, 4, 4, 'Suy luan duoc bai hoc rut ra', NOW() - INTERVAL '37 days', false),
    (5, 5, 2, 'Doc troi chay doan van ngan', NOW() - INTERVAL '36 days', false),
    (6, 6, 3, 'Hieu moi quan he nhan qua trong truyen', NOW() - INTERVAL '35 days', false),
    (7, 7, 1, 'Nhan biet tu vung co ban', NOW() - INTERVAL '34 days', false),
    (8, 8, 5, 'Phan tich duoc dong co nhan vat', NOW() - INTERVAL '33 days', false),
    (9, 9, 2, 'Doc hieu cau don gian', NOW() - INTERVAL '32 days', false),
    (10, 10, 3, 'Ke lai duoc cau chuyen bang loi cua minh', NOW() - INTERVAL '18 days', false)
ON CONFLICT ("Id") DO NOTHING;
