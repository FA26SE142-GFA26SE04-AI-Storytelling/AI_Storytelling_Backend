-- 6. organization_permissions (10)
INSERT INTO organization_permissions ("Id", "OrganizationMembershipId", "Permission", "GrantedByUserId", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 'CreateClassGroup', 1, NOW() - INTERVAL '48 days', false),
    (2, 2, 'ManageClassMembers', 1, NOW() - INTERVAL '47 days', false),
    (3, 3, 'InviteTeacher', 1, NOW() - INTERVAL '46 days', false),
    (4, 4, 'RemoveTeacher', 1, NOW() - INTERVAL '45 days', false),
    (5, 5, 'EditOrgSafetyPolicy', 1, NOW() - INTERVAL '19 days', false),
    (6, 6, 'EditOrgInfo', 1, NOW() - INTERVAL '58 days', false),
    (7, 7, 'ViewOrgDashboard', 1, NOW() - INTERVAL '56 days', false),
    (8, 8, 'CreateClassGroup', 1, NOW() - INTERVAL '20 days', false),
    (9, 1, 'ViewOrgDashboard', 1, NOW() - INTERVAL '48 days', false),
    (10, 6, 'ViewOrgDashboard', 1, NOW() - INTERVAL '58 days', false)
ON CONFLICT ("Id") DO NOTHING;
