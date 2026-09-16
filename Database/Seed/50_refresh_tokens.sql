-- 50. refresh_tokens (5)
INSERT INTO refresh_tokens ("Id", "UserAccountId", "TokenHash", "SessionScope", "IssuedAt", "ExpiresAt", "RevokedAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, encode(sha256('demo-rt-admin-1'::bytea), 'hex'), 'Supervisor', NOW() - INTERVAL '2 hours', NOW() + INTERVAL '7 days', NULL, NOW() - INTERVAL '2 hours', false),
    (2, 2, encode(sha256('demo-rt-parent1-1'::bytea), 'hex'), 'Supervisor', NOW() - INTERVAL '5 days', NOW() + INTERVAL '7 days', NULL, NOW() - INTERVAL '5 days', false),
    (3, 3, encode(sha256('demo-rt-parent2-1'::bytea), 'hex'), 'Supervisor', NOW() - INTERVAL '1 day', NOW() + INTERVAL '6 days', NULL, NOW() - INTERVAL '1 day', false),
    (4, 7, encode(sha256('demo-rt-teacher1-1'::bytea), 'hex'), 'Supervisor', NOW() - INTERVAL '3 hours', NOW() + INTERVAL '7 days', NULL, NOW() - INTERVAL '3 hours', false),
    (5, 1, encode(sha256('demo-rt-admin-old'::bytea), 'hex'), 'Admin', NOW() - INTERVAL '10 days', NOW() - INTERVAL '3 days', NOW() - INTERVAL '3 days', NOW() - INTERVAL '10 days', false)
ON CONFLICT ("Id") DO NOTHING;
