-- 51. supervision_permission_requests (8)
-- Request 1 dang cho Owner xu ly; request 2 da chap nhan; request 3/4 bi tu choi;
-- request 5 do chinh Additional Supervisor huy khi con Pending (BR-1.14).
INSERT INTO supervision_permission_requests (
    "Id", "SupervisionRelationshipId", "RequesterUserId", "Status",
    "RespondedAt", "RespondedByUserId", "CreatedAt", "UpdatedAt", "IsDeleted"
)
VALUES
    (1, 11, 3, 'Pending', NULL, NULL, NOW() - INTERVAL '3 days', NULL, false),
    (2, 12, 7, 'Accepted', NOW() - INTERVAL '2 days', 2, NOW() - INTERVAL '4 days', NOW() - INTERVAL '2 days', false),
    (3, 13, 2, 'Rejected', NOW() - INTERVAL '1 day', 3, NOW() - INTERVAL '2 days', NOW() - INTERVAL '1 day', false),
    (4, 14, 8, 'Rejected', NOW() - INTERVAL '18 hours', 3, NOW() - INTERVAL '1 day', NOW() - INTERVAL '18 hours', false),
    (5, 11, 3, 'Cancelled', NOW() - INTERVAL '8 hours', 3, NOW() - INTERVAL '10 hours', NOW() - INTERVAL '8 hours', false),
    (6, 15, 2, 'Accepted', NOW() - INTERVAL '6 hours', 3, NOW() - INTERVAL '1 day', NOW() - INTERVAL '6 hours', false),
    (7, 17, 3, 'Accepted', NOW() - INTERVAL '3 hours', 8, NOW() - INTERVAL '12 hours', NOW() - INTERVAL '3 hours', false),
    (8, 14, 8, 'Pending', NULL, NULL, NOW() - INTERVAL '2 hours', NULL, false)
ON CONFLICT ("Id") DO NOTHING;
