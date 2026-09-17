-- 9. org_consent_records (5 - chi cho child_profiles co OrganizationId)
INSERT INTO org_consent_records ("Id", "ChildProfileId", "OrganizationId", "DecidedByUserId", "DecidedAt", "Status", "CreatedAt", "IsDeleted")
VALUES
    (1, 2, 1, 2, NOW() - INTERVAL '38 days', 'Accepted', NOW() - INTERVAL '39 days', false),
    (2, 4, 2, 3, NOW() - INTERVAL '36 days', 'Accepted', NOW() - INTERVAL '37 days', false),
    (3, 6, 1, 4, NOW() - INTERVAL '34 days', 'Accepted', NOW() - INTERVAL '35 days', false),
    (4, 8, 2, NULL, NULL, 'Pending', NOW() - INTERVAL '33 days', false),
    (5, 10, 3, NULL, NULL, 'Pending', NOW() - INTERVAL '18 days', false)
ON CONFLICT ("Id") DO NOTHING;
