-- 10. child_sessions (6) - gom ca phien con hoat dong va da vuot idle timeout 20 phut.
INSERT INTO child_sessions ("Id", "ChildProfileId", "SessionKey", "LastActivityAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, '00000000000000000000000000000001', NOW() - INTERVAL '5 minutes', NOW() - INTERVAL '30 minutes', false),
    (2, 2, '00000000000000000000000000000002', NOW() - INTERVAL '25 minutes', NOW() - INTERVAL '2 hours', false),
    (3, 3, '00000000000000000000000000000003', NOW() - INTERVAL '2 minutes', NOW() - INTERVAL '10 minutes', false),
    (4, 4, '00000000000000000000000000000004', NOW() - INTERVAL '1 hour', NOW() - INTERVAL '3 hours', false),
    (5, 5, '00000000000000000000000000000005', NOW() - INTERVAL '19 minutes', NOW() - INTERVAL '40 minutes', false),
    (6, 6, '00000000000000000000000000000006', NOW() - INTERVAL '20 minutes', NOW() - INTERVAL '45 minutes', false)
ON CONFLICT ("Id") DO NOTHING;
