-- 53. ownership_transfer_requests (10)
-- Pending: Owner tao, Additional Supervisor phan hoi.
-- Cancelled: de nghi bi huy (nguoi nhan roi ho so, Owner doi...). Khong con TTL.
INSERT INTO ownership_transfer_requests (
    "Id", "ChildProfileId", "CurrentOwnerUserId", "TargetSupervisorUserId",
    "Status", "RespondedAt", "CreatedAt", "UpdatedAt", "IsDeleted"
)
VALUES
    (1, 1, 2, 3, 'Pending', NULL, NOW() - INTERVAL '2 hours', NULL, false),
    (2, 2, 2, 7, 'Cancelled', NOW() - INTERVAL '2 hours', NOW() - INTERVAL '3 hours', NOW() - INTERVAL '2 hours', false),
    (3, 3, 3, 2, 'Rejected', NOW() - INTERVAL '1 day', NOW() - INTERVAL '2 days', NOW() - INTERVAL '1 day', false),
    (4, 4, 3, 8, 'Rejected', NOW() - INTERVAL '12 hours', NOW() - INTERVAL '1 day', NOW() - INTERVAL '12 hours', false),
    (5, 11, 2, 3, 'Accepted', NOW() - INTERVAL '6 hours', NOW() - INTERVAL '18 hours', NOW() - INTERVAL '6 hours', false),
    (6, 12, 3, 8, 'Accepted', NOW() - INTERVAL '3 hours', NOW() - INTERVAL '6 hours', NOW() - INTERVAL '3 hours', false),
    (7, 2, 2, 7, 'Rejected', NOW() - INTERVAL '10 days', NOW() - INTERVAL '11 days', NOW() - INTERVAL '10 days', false),
    (8, 3, 3, 2, 'Cancelled', NOW() - INTERVAL '8 days', NOW() - INTERVAL '9 days', NOW() - INTERVAL '8 days', false),
    (9, 4, 3, 8, 'Pending', NULL, NOW() - INTERVAL '1 hour', NULL, false),
    (10, 11, 3, 2, 'Rejected', NOW() - INTERVAL '9 days', NOW() - INTERVAL '10 days', NOW() - INTERVAL '9 days', false)
ON CONFLICT ("Id") DO NOTHING;
