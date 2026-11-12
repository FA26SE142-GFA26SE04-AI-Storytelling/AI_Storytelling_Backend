-- 58. story_wish_card_matches (16): Moderator gan the uoc cho truyen (WF-L2D UC-L2D-03).
-- Source=Ai mappings are created by the real ai-match API, not fabricated by this fixture.
-- Truyen rieng cua phu huynh: Truyen 1: Chu meo, Khu rung, Bi lac duong. Truyen 3: Co be phi hanh gia, Truong hoc.
-- Truyen Creator 6-8 (de xem storyImpact khi duyet go / cong khai lai):
--   Truyen 16 (dang cong khai, dung de test xin go): Chu meo, Meo Mun, Tinh ban, Biet lang nghe.
--     Khi go 16: Chu meo / Meo Mun / Biet lang nghe khong con truyen Creator cong khai nao khac (0);
--     Tinh ban con truyen 13 va 14 (2). Truyen 1 la truyen rieng, truyen 17 da go nen khong tinh.
--   Truyen 13 (dang cong khai): Truong hoc, Chia se, Tinh ban.
--   Truyen 14 (dang cong khai, co muc 21 PublicStoryReview): Giup ban, Tinh ban -> claim muc 21 co wishCardsNeedReview = true.
--   Truyen 17 (da duoc Moderator go): Chu tho, Tinh ban -> the van giu sau khi go.
INSERT INTO story_wish_card_matches ("Id", "StoryId", "WishCardId", "Source", "CreatedByUserId", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 1, 'Moderator', NULL, NOW() - INTERVAL '5 days', false),
    (2, 1, 5, 'Moderator', NULL, NOW() - INTERVAL '5 days', false),
    (3, 1, 9, 'Moderator', NULL, NOW() - INTERVAL '5 days', false),
    (4, 3, 2, 'Moderator', NULL, NOW() - INTERVAL '5 days', false),
    (5, 3, 7, 'Moderator', NULL, NOW() - INTERVAL '5 days', false),
    (6, 16, 1, 'Moderator', 11, NOW() - INTERVAL '4 days', false),
    (7, 16, 26, 'Moderator', 11, NOW() - INTERVAL '4 days', false),
    (8, 16, 16, 'Moderator', 11, NOW() - INTERVAL '4 days', false),
    (9, 16, 25, 'Moderator', 11, NOW() - INTERVAL '4 days', false),
    (10, 13, 7, 'Moderator', 11, NOW() - INTERVAL '4 days', false),
    (11, 13, 13, 'Moderator', 11, NOW() - INTERVAL '4 days', false),
    (12, 13, 16, 'Moderator', 11, NOW() - INTERVAL '4 days', false),
    (13, 14, 10, 'Moderator', 12, NOW() - INTERVAL '4 days', false),
    (14, 14, 16, 'Moderator', 12, NOW() - INTERVAL '4 days', false),
    (15, 17, 4, 'Moderator', 12, NOW() - INTERVAL '4 days', false),
    (16, 17, 16, 'Moderator', 12, NOW() - INTERVAL '4 days', false)
ON CONFLICT ("Id") DO NOTHING;
