-- 8. child_profiles (12)
INSERT INTO child_profiles ("Id", "OwnerUserId", "OrganizationId", "Nickname", "AgeBand", "DateOfBirth", "Language", "Scope", "Status", "CreatedAt", "IsDeleted")
VALUES
    (1, 2, NULL, 'Be An', 'Age_6_8', (NOW() - INTERVAL '7 years')::date, 'vi', 'Personal', 'Active', NOW() - INTERVAL '40 days', false),
    (2, 2, 1, 'Be Binh', 'Age_9_12', (NOW() - INTERVAL '10 years')::date, 'vi', 'Organization', 'Active', NOW() - INTERVAL '39 days', false),
    (3, 3, NULL, 'Be Chi', 'Age_6_8', (NOW() - INTERVAL '7 years')::date, 'vi', 'Personal', 'Active', NOW() - INTERVAL '38 days', false),
    (4, 3, 2, 'Be Dat', 'Age_9_12', (NOW() - INTERVAL '10 years')::date, 'vi', 'Organization', 'Active', NOW() - INTERVAL '37 days', false),
    (5, 4, NULL, 'Be Em', 'Age_6_8', (NOW() - INTERVAL '6 years')::date, 'vi', 'Personal', 'PendingParentConsent', NOW() - INTERVAL '36 days', false),
    (6, 4, 1, 'Be Phuc', 'Age_9_12', (NOW() - INTERVAL '11 years')::date, 'vi', 'Organization', 'Active', NOW() - INTERVAL '35 days', false),
    (7, 5, NULL, 'Be Giang', 'Age_6_8', (NOW() - INTERVAL '8 years')::date, 'vi', 'Personal', 'Active', NOW() - INTERVAL '34 days', false),
    (8, 5, 2, 'Be Hanh', 'Age_9_12', (NOW() - INTERVAL '10 years')::date, 'vi', 'Organization', 'Active', NOW() - INTERVAL '33 days', false),
    (9, 6, NULL, 'Be Y', 'Age_6_8', (NOW() - INTERVAL '7 years')::date, 'vi', 'Personal', 'Draft', NOW() - INTERVAL '32 days', false),
    (10, 6, 3, 'Be Khang', 'Age_9_12', (NOW() - INTERVAL '9 years')::date, 'vi', 'Organization', 'PendingSupervision', NOW() - INTERVAL '18 days', false),
    (11, 3, NULL, 'Be Lam', 'Age_6_8', (NOW() - INTERVAL '8 years')::date, 'vi', 'Personal', 'Active', NOW() - INTERVAL '16 days', false),
    (12, 8, NULL, 'Be Minh', 'Age_9_12', (NOW() - INTERVAL '11 years')::date, 'vi', 'Personal', 'Active', NOW() - INTERVAL '15 days', false)
ON CONFLICT ("Id") DO NOTHING;
