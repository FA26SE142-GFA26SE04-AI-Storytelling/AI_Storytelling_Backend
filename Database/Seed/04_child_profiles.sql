-- 4. child_profiles (12)
INSERT INTO child_profiles (
    "Id", "OwnerUserId", "Nickname", "AgeBand", "Language", "Status", "CreatedAt", "IsDeleted", "AvatarId"
)
VALUES
    (1, 2, 'Bé An', 'Age_6_8', 'vi', 'Active', NOW() - INTERVAL '40 days', false, 'avatar-rabbit'),
    (2, 2, 'Bé Bình', 'Age_9_10', 'vi', 'Active', NOW() - INTERVAL '39 days', false, 'avatar-tiger'),
    (3, 3, 'Bé Chi', 'Age_6_8', 'vi', 'Active', NOW() - INTERVAL '38 days', false, 'avatar-bear'),
    (4, 3, 'Bé Đạt', 'Age_9_10', 'vi', 'Active', NOW() - INTERVAL '37 days', false, 'avatar-dolphin'),
    (5, 4, 'Bé Em', 'Age_6_8', 'vi', 'Draft', NOW() - INTERVAL '36 days', false, 'avatar-cat'),
    (6, 4, 'Bé Phúc', 'Age_9_10', 'vi', 'Active', NOW() - INTERVAL '35 days', false, 'avatar-whale'),
    (7, 5, 'Bé Giang', 'Age_6_8', 'vi', 'Active', NOW() - INTERVAL '34 days', false, 'avatar-fox'),
    (8, 5, 'Bé Hạnh', 'Age_9_10', 'vi', 'Active', NOW() - INTERVAL '33 days', false, 'avatar-lion'),
    (9, 6, 'Bé Ý', 'Age_6_8', 'vi', 'Draft', NOW() - INTERVAL '32 days', false, 'avatar-owl'),
    (10, 6, 'Bé Khang', 'Age_9_10', 'vi', 'Draft', NOW() - INTERVAL '18 days', false, 'avatar-eagle'),
    (11, 3, 'Bé Lâm', 'Age_6_8', 'vi', 'Active', NOW() - INTERVAL '16 days', false, 'avatar-panda'),
    (12, 8, 'Bé Minh', 'Age_9_10', 'vi', 'Active', NOW() - INTERVAL '15 days', false, 'avatar-koala')
ON CONFLICT ("Id") DO NOTHING;
