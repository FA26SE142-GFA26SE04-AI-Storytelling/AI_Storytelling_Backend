-- 9. child_access_credentials (12) - mot credential cho moi child profile.
-- PIN demo theo thu tu Id: 1234, 5678, 1111, 2222, 9999, 2468, 1357, 4321, 8080, 9090, 1122, 3344.
-- PinHash dung BCrypt work factor 11, khop StoryPlatform.Infrastructure.Security.PasswordHasher.
INSERT INTO child_access_credentials (
    "Id", "ChildProfileId", "AvatarId", "PinHash", "FailedAttempts", "LockedUntil",
    "EasyLoginCode", "EasyLoginExpiresAt", "EasyLoginUsedAt",
    "CreatedByUserId", "CreatedAt", "IsDeleted"
)
VALUES
    (1, 1, 'avatar-rabbit', '$2a$11$jaEZQ/HGWozW517gaf.FfO0p2oU7R.ybJjgS5y08mbuIs85QksVXe', 0, NULL,
     NULL, NULL, NULL, 2, NOW() - INTERVAL '39 days', false),
    (2, 2, 'avatar-tiger', '$2a$11$ZUyIDsDkGQPuzfxtdTji9u0r5YkaemnbJi32cEfucTxUVO4eBoULK', 0, NULL,
     'seed-expired-easy-login-profile-2', NOW() - INTERVAL '1 day', NULL, 2, NOW() - INTERVAL '38 days', false),
    (3, 3, 'avatar-bear', '$2a$11$vY2gL2D6JsXwXiUE/3SPQO0rkXki750QTFo8Xw6h1vAt0xyZYZ6.y', 0, NULL,
     NULL, NULL, NOW() - INTERVAL '2 days', 3, NOW() - INTERVAL '37 days', false),
    (4, 4, 'avatar-dolphin', '$2a$11$wVhXK1X3jTmlj/YSW1/GyOMtYAMNQWyZ4jCW/cvNGvtpgCC/DFabm', 2, NULL,
     NULL, NULL, NULL, 3, NOW() - INTERVAL '36 days', false),
    (5, 5, 'avatar-cat', '$2a$11$LNFo8nDrifxMrylGlVj1TOfJsMTIvjy1QQ7VmmWWxgEjBXMnIEzmC', 0, NOW() + INTERVAL '5 minutes',
     NULL, NULL, NULL, 4, NOW() - INTERVAL '35 days', false),
    (6, 6, 'avatar-whale', '$2a$11$I6ArOMsGQIinGMMCO4IEAuyj1I6QYx9CSwoATeKg9.KjOPazSDU0.', 0, NULL,
     NULL, NULL, NULL, 4, NOW() - INTERVAL '34 days', false),
    (7, 7, 'avatar-fox', '$2a$11$8VwAmF2ftLH6/bXr/rgIdOoXB0yS3s243kE6nvRhnCxKE8zSWY4bu', 0, NULL,
     NULL, NULL, NULL, 5, NOW() - INTERVAL '33 days', false),
    (8, 8, 'avatar-lion', '$2a$11$.7JRE1lBTOn6lt.OuH90k.5kh840gIBZdbLNySVaEOa9GXHyUZMZi', 0, NULL,
     NULL, NULL, NULL, 5, NOW() - INTERVAL '32 days', false),
    (9, 9, 'avatar-owl', '$2a$11$3nu/5E9rVAXc1Q4A95lFb.bKoIft1MHQBxG0bP00zb1Y0rez3xatC', 0, NULL,
     NULL, NULL, NULL, 6, NOW() - INTERVAL '31 days', false),
    (10, 10, 'avatar-eagle', '$2a$11$EfEozDMqNVNoWHjV/taNNOArgpZGDgKPbZiQEg0KA1w0gmlEKfTE6', 0, NULL,
     NULL, NULL, NULL, 6, NOW() - INTERVAL '17 days', false),
    (11, 11, 'avatar-panda', '$2a$11$JwcOZu3/Nu1NEEJ/7sPa0e6A0PPMJsb8uzfvqPMVQBSuOC70fkNga', 0, NULL,
     NULL, NULL, NULL, 3, NOW() - INTERVAL '15 days', false),
    (12, 12, 'avatar-koala', '$2a$11$uhM9PFp8FgPXbSB7X4MpnOVrUBB0h0d4jwkt694tGjADVDan0Qw6.', 0, NULL,
     NULL, NULL, NULL, 8, NOW() - INTERVAL '14 days', false)
ON CONFLICT ("Id") DO NOTHING;
