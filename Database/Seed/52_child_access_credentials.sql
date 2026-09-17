-- 52. child_access_credentials (5)
INSERT INTO child_access_credentials ("Id", "ChildProfileId", "AvatarId", "PinHash", "FailedAttempts", "LockedUntil", "CreatedByUserId", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 'avatar-rabbit', encode(sha256('1234'::bytea), 'hex'), 0, NULL, 2, NOW() - INTERVAL '39 days', false),
    (2, 3, 'avatar-bear', encode(sha256('5678'::bytea), 'hex'), 0, NULL, 3, NOW() - INTERVAL '37 days', false),
    (3, 5, 'avatar-cat', encode(sha256('1111'::bytea), 'hex'), 2, NULL, 4, NOW() - INTERVAL '35 days', false),
    (4, 7, 'avatar-fox', encode(sha256('2222'::bytea), 'hex'), 0, NULL, 5, NOW() - INTERVAL '33 days', false),
    (5, 9, 'avatar-owl', encode(sha256('9999'::bytea), 'hex'), 5, NOW() + INTERVAL '10 minutes', 6, NOW() - INTERVAL '31 days', false)
ON CONFLICT ("Id") DO NOTHING;
