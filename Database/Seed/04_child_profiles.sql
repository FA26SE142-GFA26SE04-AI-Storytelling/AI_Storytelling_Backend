-- 4. child_profiles (12)
INSERT INTO child_profiles ("Id", "OwnerUserId", "Nickname", "AgeBand", "DateOfBirth", "Language", "Status", "CreatedAt", "IsDeleted")
VALUES
    (1, 2, 'Be An', 'Age_6_8', (NOW() - INTERVAL '7 years')::date, 'vi', 'Active', NOW() - INTERVAL '40 days', false),
    (2, 2, 'Be Binh', 'Age_9_12', (NOW() - INTERVAL '10 years')::date, 'vi', 'Active', NOW() - INTERVAL '39 days', false),
    (3, 3, 'Be Chi', 'Age_6_8', (NOW() - INTERVAL '7 years')::date, 'vi', 'Active', NOW() - INTERVAL '38 days', false),
    (4, 3, 'Be Dat', 'Age_9_12', (NOW() - INTERVAL '10 years')::date, 'vi', 'Active', NOW() - INTERVAL '37 days', false),
    (5, 4, 'Be Em', 'Age_6_8', (NOW() - INTERVAL '6 years')::date, 'vi', 'PendingParentConsent', NOW() - INTERVAL '36 days', false),
    (6, 4, 'Be Phuc', 'Age_9_12', (NOW() - INTERVAL '11 years')::date, 'vi', 'Active', NOW() - INTERVAL '35 days', false),
    (7, 5, 'Be Giang', 'Age_6_8', (NOW() - INTERVAL '8 years')::date, 'vi', 'Active', NOW() - INTERVAL '34 days', false),
    (8, 5, 'Be Hanh', 'Age_9_12', (NOW() - INTERVAL '10 years')::date, 'vi', 'Active', NOW() - INTERVAL '33 days', false),
    (9, 6, 'Be Y', 'Age_6_8', (NOW() - INTERVAL '7 years')::date, 'vi', 'Draft', NOW() - INTERVAL '32 days', false),
    (10, 6, 'Be Khang', 'Age_9_12', (NOW() - INTERVAL '9 years')::date, 'vi', 'PendingSupervision', NOW() - INTERVAL '18 days', false),
    (11, 3, 'Be Lam', 'Age_6_8', (NOW() - INTERVAL '8 years')::date, 'vi', 'Active', NOW() - INTERVAL '16 days', false),
    (12, 8, 'Be Minh', 'Age_9_12', (NOW() - INTERVAL '11 years')::date, 'vi', 'Active', NOW() - INTERVAL '15 days', false)
ON CONFLICT ("Id") DO NOTHING;
