-- 4. organization_memberships (8)
INSERT INTO organization_memberships ("Id", "OrganizationId", "UserId", "OrgRole", "Status", "InvitedByUserId", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 7, 'Teacher', 'Active', 1, NOW() - INTERVAL '48 days', false),
    (2, 1, 8, 'Teacher', 'Active', 1, NOW() - INTERVAL '47 days', false),
    (3, 2, 9, 'Teacher', 'Active', 1, NOW() - INTERVAL '46 days', false),
    (4, 2, 10, 'Teacher', 'Active', 1, NOW() - INTERVAL '45 days', false),
    (5, 3, 7, 'Teacher', 'Pending', 1, NOW() - INTERVAL '19 days', false),
    (6, 1, 1, 'SchoolAdmin', 'Active', 1, NOW() - INTERVAL '58 days', false),
    (7, 2, 1, 'SchoolAdmin', 'Active', 1, NOW() - INTERVAL '56 days', false),
    (8, 3, 1, 'SchoolAdmin', 'Active', 1, NOW() - INTERVAL '20 days', false)
ON CONFLICT ("Id") DO NOTHING;
