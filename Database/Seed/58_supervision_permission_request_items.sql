-- 58. supervision_permission_request_items (7)
INSERT INTO supervision_permission_request_items (
    "Id", "SupervisionPermissionRequestId", "Permission", "CreatedAt", "UpdatedAt", "IsDeleted"
)
VALUES
    (1, 1, 'ViewProgress', NOW() - INTERVAL '3 days', NULL, false),
    (2, 1, 'ViewResults', NOW() - INTERVAL '3 days', NULL, false),
    (3, 2, 'ViewProgress', NOW() - INTERVAL '4 days', NULL, false),
    (4, 2, 'ReceiveReport', NOW() - INTERVAL '4 days', NULL, false),
    (5, 3, 'ApproveStory', NOW() - INTERVAL '2 days', NULL, false),
    (6, 4, 'ManageSafetySettings', NOW() - INTERVAL '1 day', NULL, false),
    (7, 4, 'GenerateStory', NOW() - INTERVAL '1 day', NULL, false)
ON CONFLICT ("Id") DO NOTHING;
