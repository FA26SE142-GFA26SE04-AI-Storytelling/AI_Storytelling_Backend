-- 52. data_requests (10)
INSERT INTO data_requests ("Id", "ChildProfileId", "RequestedByUserId", "RequestType", "Status", "DeletionMethod", "LegalBasisNote", "ResolvedByUserId", "ResolvedAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 2, 'Export', 'Resolved', NULL, NULL, 1, NOW() - INTERVAL '1 days', NOW() - INTERVAL '2 days', false),
    (2, 2, 2, 'Delete', 'Pending', 'Anonymize', NULL, NULL, NULL, NOW() - INTERVAL '2 days', false),
    (3, 3, 3, 'Export', 'Resolved', NULL, NULL, 1, NOW() - INTERVAL '3 days', NOW() - INTERVAL '4 days', false),
    (4, 4, 3, 'Delete', 'Pending', 'Anonymize', NULL, NULL, NULL, NOW() - INTERVAL '3 days', false),
    (5, 5, 4, 'Export', 'Resolved', NULL, NULL, 1, NOW() - INTERVAL '4 days', NOW() - INTERVAL '5 days', false),
    (6, 6, 4, 'Export', 'Pending', NULL, NULL, NULL, NULL, NOW() - INTERVAL '4 days', false),
    (7, 7, 5, 'Delete', 'Resolved', 'Anonymize', NULL, 1, NOW() - INTERVAL '5 days', NOW() - INTERVAL '6 days', false),
    (8, 8, 5, 'Export', 'Pending', NULL, NULL, NULL, NULL, NOW() - INTERVAL '5 days', false),
    (9, 9, 6, 'Delete', 'Resolved', 'Anonymize', NULL, 1, NOW() - INTERVAL '6 days', NOW() - INTERVAL '7 days', false),
    (10, 10, 6, 'Export', 'Pending', NULL, NULL, NULL, NULL, NOW() - INTERVAL '2 days', false)
ON CONFLICT ("Id") DO NOTHING;
