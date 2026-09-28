-- 10. child_sessions (6) - gom ca phien con hoat dong va da vuot idle timeout 20 phut.
-- Buoc 3.0: moi phien co dung 1 nguon loi vao; seed dung loi vao doc lap (credential cung Id voi ho so).
INSERT INTO child_sessions ("Id", "ChildProfileId", "ChildAccessCredentialId", "SupervisorSessionId", "SessionKey", "LastActivityAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 1, NULL, '00000000000000000000000000000001', NOW() - INTERVAL '5 minutes', NOW() - INTERVAL '30 minutes', false),
    (2, 2, 2, NULL, '00000000000000000000000000000002', NOW() - INTERVAL '25 minutes', NOW() - INTERVAL '2 hours', false),
    (3, 3, 3, NULL, '00000000000000000000000000000003', NOW() - INTERVAL '2 minutes', NOW() - INTERVAL '10 minutes', false),
    (4, 4, 4, NULL, '00000000000000000000000000000004', NOW() - INTERVAL '1 hour', NOW() - INTERVAL '3 hours', false),
    (5, 5, 5, NULL, '00000000000000000000000000000005', NOW() - INTERVAL '19 minutes', NOW() - INTERVAL '40 minutes', false),
    (6, 6, 6, NULL, '00000000000000000000000000000006', NOW() - INTERVAL '20 minutes', NOW() - INTERVAL '45 minutes', false)
ON CONFLICT ("Id") DO NOTHING;
