-- 9. child_access_credentials (12) - mot credential Easy Login cho moi child profile.
-- Secret demo (CHI DUNG DEV/QA, KHONG dung production): demo-qr-child-01 ... demo-qr-child-12 theo thu tu Id.
-- EasyLoginSecretHash = SHA-256 (hex thuong) cua secret, khop StoryPlatform.Application.Common.Security.TokenHasher.
-- Secret that la chuoi ngau nhien 256-bit do he thong sinh khi Supervisor tao Easy Login.
INSERT INTO child_access_credentials (
    "Id", "ChildProfileId", "AvatarId", "EasyLoginSecretHash", "EasyLoginCreatedAt",
    "CreatedByUserId", "CreatedAt", "IsDeleted"
)
VALUES
    (1, 1, 'avatar-rabbit', 'c9274b0a813dcbb01d77b116ab865e8e528823a644a91cf4aed8d0304ffca19b',
     NOW() - INTERVAL '39 days', 2, NOW() - INTERVAL '39 days', false),
    (2, 2, 'avatar-tiger', '07ffdc955d64d0b6d06ae706f34b5e66362f8681962a033c17ec5eb3c9e7f1ea',
     NOW() - INTERVAL '38 days', 2, NOW() - INTERVAL '38 days', false),
    (3, 3, 'avatar-bear', '1287a93953f5b244de878a6016b32923c02e4fd6de808b04c051a2c0bebbb382',
     NOW() - INTERVAL '37 days', 3, NOW() - INTERVAL '37 days', false),
    (4, 4, 'avatar-dolphin', '6c046d5789039713bd7f4e2b7059e872fb1f6c875cb6fbc93fdd4668c09c8ab2',
     NOW() - INTERVAL '36 days', 3, NOW() - INTERVAL '36 days', false),
    (5, 5, 'avatar-cat', 'bb4602e3f587b5b0d4bd23e77a9b3e6c6629e781dbc122d968ab08c53aa31d2e',
     NOW() - INTERVAL '35 days', 4, NOW() - INTERVAL '35 days', false),
    (6, 6, 'avatar-whale', '1f3930b167727d6f2fed8138f3800c64cfa3194726682ce58d2b82feda4e89df',
     NOW() - INTERVAL '34 days', 4, NOW() - INTERVAL '34 days', false),
    (7, 7, 'avatar-fox', '22c380da40bf0f34b743163796f332f345fc297990fcbafe24719843c6a49272',
     NOW() - INTERVAL '33 days', 5, NOW() - INTERVAL '33 days', false),
    (8, 8, 'avatar-lion', '621223c5f7cac1b2022bce87c1f16c8b4c4862501f7a6aa501eea4a31fb9b3b8',
     NOW() - INTERVAL '32 days', 5, NOW() - INTERVAL '32 days', false),
    (9, 9, 'avatar-owl', '9f4de44478075d1a73a4116efb64d57b510307d7495d22e520dc9ee26fd569b1',
     NOW() - INTERVAL '31 days', 6, NOW() - INTERVAL '31 days', false),
    (10, 10, 'avatar-eagle', '132fb8223992d7ed0b78a086feee22ae032c7b96987c0d507832720be603cf64',
     NOW() - INTERVAL '17 days', 6, NOW() - INTERVAL '17 days', false),
    (11, 11, 'avatar-panda', 'd7868f28fef3f7ad49a447483454b1970864515bdbae472358401a4a95b89f60',
     NOW() - INTERVAL '15 days', 3, NOW() - INTERVAL '15 days', false),
    (12, 12, 'avatar-koala', '22e4c35cbf378f632f268dfdb5c4caabe7a63771dde67ca57daf2fcff1d16d99',
     NOW() - INTERVAL '14 days', 8, NOW() - INTERVAL '14 days', false)
ON CONFLICT ("Id") DO NOTHING;
