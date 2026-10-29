-- 64. story_wish_card_matches (5): Moderator gan the uoc cho truyen (tam thay AI, WF-L2D UC-L2D-03).
-- Truyen 1: Chu meo, Khu rung, Bi lac duong. Truyen 3: Co be phi hanh gia, Truong hoc.
INSERT INTO story_wish_card_matches ("Id", "StoryId", "WishCardId", "Source", "CreatedByUserId", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 1, 'Moderator', NULL, NOW() - INTERVAL '5 days', false),
    (2, 1, 5, 'Moderator', NULL, NOW() - INTERVAL '5 days', false),
    (3, 1, 9, 'Moderator', NULL, NOW() - INTERVAL '5 days', false),
    (4, 3, 2, 'Moderator', NULL, NOW() - INTERVAL '5 days', false),
    (5, 3, 7, 'Moderator', NULL, NOW() - INTERVAL '5 days', false)
ON CONFLICT ("Id") DO NOTHING;
