-- 44. supervision_relationships (10)
INSERT INTO supervision_relationships ("Id", "ChildProfileId", "SupervisorUserId", "SupervisorRole", "SupervisionInvitationId", "RevokedAt", "RevokedByUserId", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 2, 'Owner', 1, NULL, NULL, NOW() - INTERVAL '39 days', false),
    (2, 2, 2, 'Owner', NULL, NULL, NULL, NOW() - INTERVAL '39 days', false),
    (3, 3, 3, 'Owner', 3, NULL, NULL, NOW() - INTERVAL '37 days', false),
    (4, 4, 3, 'Owner', NULL, NULL, NULL, NOW() - INTERVAL '37 days', false),
    (5, 5, 4, 'AdditionalSupervisor', NULL, NULL, NULL, NOW() - INTERVAL '36 days', false),
    (6, 6, 4, 'Owner', 6, NULL, NULL, NOW() - INTERVAL '34 days', false),
    (7, 7, 5, 'Owner', NULL, NULL, NULL, NOW() - INTERVAL '34 days', false),
    (8, 8, 5, 'Owner', 8, NULL, NULL, NOW() - INTERVAL '32 days', false),
    (9, 9, 6, 'AdditionalSupervisor', NULL, NOW() - INTERVAL '20 days', 6, NOW() - INTERVAL '31 days', false),
    (10, 10, 6, 'Owner', 10, NULL, NULL, NOW() - INTERVAL '17 days', false)
ON CONFLICT ("Id") DO NOTHING;
