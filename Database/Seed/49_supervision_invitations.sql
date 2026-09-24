-- 49. supervision_invitations (10)
INSERT INTO supervision_invitations ("Id", "ChildProfileId", "InviterUserId", "InviteeUserId", "InviteeEmail", "InvitationCode", "Status", "ExpiresAt", "RespondedAt", "UsedAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 2, NULL, 'supervisor1.demo@example.com', 'INVITE-DEMO-0001', 'Accepted', NOW() - INTERVAL '33 days', NOW() - INTERVAL '39 days', NOW() - INTERVAL '39 days', NOW() - INTERVAL '40 days', false),
    (2, 2, 2, NULL, 'supervisor2.demo@example.com', 'INVITE-DEMO-0002', 'Pending', NOW() + INTERVAL '2 days', NULL, NULL, NOW() - INTERVAL '1 day', false),
    (3, 3, 3, NULL, 'supervisor3.demo@example.com', 'INVITE-DEMO-0003', 'Accepted', NOW() - INTERVAL '31 days', NOW() - INTERVAL '37 days', NOW() - INTERVAL '37 days', NOW() - INTERVAL '38 days', false),
    (4, 4, 3, NULL, 'supervisor4.demo@example.com', 'INVITE-DEMO-0004', 'Rejected', NOW() - INTERVAL '30 days', NOW() - INTERVAL '36 days', NULL, NOW() - INTERVAL '37 days', false),
    (5, 5, 4, NULL, 'supervisor5.demo@example.com', 'INVITE-DEMO-0005', 'Expired', NOW() - INTERVAL '29 days', NULL, NULL, NOW() - INTERVAL '36 days', false),
    (6, 6, 4, NULL, 'supervisor6.demo@example.com', 'INVITE-DEMO-0006', 'Accepted', NOW() - INTERVAL '28 days', NOW() - INTERVAL '34 days', NOW() - INTERVAL '34 days', NOW() - INTERVAL '35 days', false),
    (7, 7, 5, NULL, 'supervisor7.demo@example.com', 'INVITE-DEMO-0007', 'Pending', NOW() + INTERVAL '5 days', NULL, NULL, NOW() - INTERVAL '2 days', false),
    (8, 8, 5, NULL, 'supervisor8.demo@example.com', 'INVITE-DEMO-0008', 'Accepted', NOW() - INTERVAL '26 days', NOW() - INTERVAL '32 days', NOW() - INTERVAL '32 days', NOW() - INTERVAL '33 days', false),
    (9, 9, 6, NULL, 'supervisor9.demo@example.com', 'INVITE-DEMO-0009', 'Revoked', NOW() - INTERVAL '25 days', NOW() - INTERVAL '31 days', NULL, NOW() - INTERVAL '32 days', false),
    (10, 10, 6, NULL, 'supervisor10.demo@example.com', 'INVITE-DEMO-0010', 'Accepted', NOW() - INTERVAL '11 days', NOW() - INTERVAL '17 days', NOW() - INTERVAL '17 days', NOW() - INTERVAL '18 days', false)
ON CONFLICT ("Id") DO NOTHING;
