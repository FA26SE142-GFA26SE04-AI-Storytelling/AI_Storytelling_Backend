-- 4. child_profiles (12)
INSERT INTO child_profiles (
    "Id", "OwnerUserId", "Nickname", "AgeBand", "Language", "Status", "CreatedAt", "IsDeleted", "AvatarId"
)
VALUES
    (1, 2, 'Be An', 'Age_6_8', 'vi', 'Active', NOW() - INTERVAL '40 days', false, 'avatar-rabbit'),
    (2, 2, 'Be Binh', 'Age_9_10', 'vi', 'Active', NOW() - INTERVAL '39 days', false, 'avatar-tiger'),
    (3, 3, 'Be Chi', 'Age_6_8', 'vi', 'Active', NOW() - INTERVAL '38 days', false, 'avatar-bear'),
    (4, 3, 'Be Dat', 'Age_9_10', 'vi', 'Active', NOW() - INTERVAL '37 days', false, 'avatar-dolphin'),
    (5, 4, 'Be Em', 'Age_6_8', 'vi', 'Draft', NOW() - INTERVAL '36 days', false, 'avatar-cat'),
    (6, 4, 'Be Phuc', 'Age_9_10', 'vi', 'Active', NOW() - INTERVAL '35 days', false, 'avatar-whale'),
    (7, 5, 'Be Giang', 'Age_6_8', 'vi', 'Active', NOW() - INTERVAL '34 days', false, 'avatar-fox'),
    (8, 5, 'Be Hanh', 'Age_9_10', 'vi', 'Active', NOW() - INTERVAL '33 days', false, 'avatar-lion'),
    (9, 6, 'Be Y', 'Age_6_8', 'vi', 'Draft', NOW() - INTERVAL '32 days', false, 'avatar-owl'),
    (10, 6, 'Be Khang', 'Age_9_10', 'vi', 'Draft', NOW() - INTERVAL '18 days', false, 'avatar-eagle'),
    (11, 3, 'Be Lam', 'Age_6_8', 'vi', 'Active', NOW() - INTERVAL '16 days', false, 'avatar-panda'),
    (12, 8, 'Be Minh', 'Age_9_10', 'vi', 'Active', NOW() - INTERVAL '15 days', false, 'avatar-koala')
ON CONFLICT ("Id") DO NOTHING;
