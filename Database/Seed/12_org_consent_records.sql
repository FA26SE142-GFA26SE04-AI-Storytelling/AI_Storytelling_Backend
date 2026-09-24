-- 12. org_consent_records (6 - gom ca consent hien tai va mot dong audit da revoke)
INSERT INTO org_consent_records ("Id", "ChildProfileId", "OrganizationId", "DecidedByUserId", "DecidedAt", "RevokedAt", "Status", "CreatedAt", "IsDeleted")
VALUES
    (1, 2, 1, 2, NOW() - INTERVAL '38 days', NULL, 'Accepted', NOW() - INTERVAL '39 days', false),
    (2, 4, 2, 3, NOW() - INTERVAL '36 days', NULL, 'Accepted', NOW() - INTERVAL '37 days', false),
    (3, 6, 1, 4, NOW() - INTERVAL '34 days', NULL, 'Accepted', NOW() - INTERVAL '35 days', false),
    (4, 8, 2, NULL, NULL, NULL, 'Pending', NOW() - INTERVAL '33 days', false),
    (5, 10, 3, NULL, NULL, NULL, 'Pending', NOW() - INTERVAL '18 days', false),
    (6, 2, 1, 2, NOW() - INTERVAL '42 days', NOW() - INTERVAL '40 days', 'Rejected', NOW() - INTERVAL '43 days', false)
ON CONFLICT ("Id") DO NOTHING;
