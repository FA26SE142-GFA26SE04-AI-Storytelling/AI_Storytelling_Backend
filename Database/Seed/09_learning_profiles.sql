-- 9. learning_profiles (10 - unique theo ChildProfileId)
INSERT INTO learning_profiles (
    "Id", "ChildProfileId", "ReadingLevel", "ComprehensionGoal", "CreatedAt", "IsDeleted", "ReadingMode"
)
VALUES
    (1, 1, 2, 'Hieu duoc y chinh cua cau chuyen', NOW() - INTERVAL '40 days', false, 'ListenFirst'),
    (2, 2, 3, 'Tom tat duoc dien bien cau chuyen', NOW() - INTERVAL '39 days', false, 'ReadFirst'),
    (3, 3, 1, 'Nhan biet nhan vat chinh', NOW() - INTERVAL '38 days', false, 'ListenFirst'),
    (4, 4, 4, 'Suy luan duoc bai hoc rut ra', NOW() - INTERVAL '37 days', false, 'ReadFirst'),
    (5, 5, 2, 'Doc troi chay doan van ngan', NOW() - INTERVAL '36 days', false, 'ListenFirst'),
    (6, 6, 3, 'Hieu moi quan he nhan qua trong truyen', NOW() - INTERVAL '35 days', false, 'ReadFirst'),
    (7, 7, 1, 'Nhan biet tu vung co ban', NOW() - INTERVAL '34 days', false, 'ListenFirst'),
    (8, 8, 5, 'Phan tich duoc dong co nhan vat', NOW() - INTERVAL '33 days', false, 'ReadFirst'),
    (9, 9, 2, 'Doc hieu cau don gian', NOW() - INTERVAL '32 days', false, 'ListenFirst'),
    (10, 10, 3, 'Ke lai duoc cau chuyen bang loi cua minh', NOW() - INTERVAL '18 days', false, 'ReadFirst')
ON CONFLICT ("Id") DO NOTHING;
