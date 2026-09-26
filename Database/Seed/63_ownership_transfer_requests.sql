-- 63. ownership_transfer_requests (6)
-- Pending: Owner tao, Additional Supervisor phan hoi.
-- PendingOwnerResponse: Additional Supervisor tao, Owner phan hoi.
INSERT INTO ownership_transfer_requests (
    "Id", "ChildProfileId", "CurrentOwnerUserId", "TargetSupervisorUserId",
    "Status", "RespondedAt", "CreatedAt", "UpdatedAt", "IsDeleted"
)
VALUES
    (1, 1, 2, 3, 'Pending', NULL, NOW() - INTERVAL '2 hours', NULL, false),
    (2, 2, 2, 7, 'PendingOwnerResponse', NULL, NOW() - INTERVAL '3 hours', NULL, false),
    (3, 3, 3, 2, 'Rejected', NOW() - INTERVAL '1 day', NOW() - INTERVAL '2 days', NOW() - INTERVAL '1 day', false),
    (4, 4, 3, 8, 'RejectedByOwner', NOW() - INTERVAL '12 hours', NOW() - INTERVAL '1 day', NOW() - INTERVAL '12 hours', false),
    (5, 11, 2, 3, 'Accepted', NOW() - INTERVAL '6 hours', NOW() - INTERVAL '18 hours', NOW() - INTERVAL '6 hours', false),
    (6, 12, 3, 8, 'AcceptedByOwner', NOW() - INTERVAL '3 hours', NOW() - INTERVAL '6 hours', NOW() - INTERVAL '3 hours', false)
ON CONFLICT ("Id") DO NOTHING;
