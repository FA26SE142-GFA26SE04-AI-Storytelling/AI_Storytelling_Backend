-- 59. story_wish_suggestions (2):
-- Dieu uoc 1 (be 1: Chu meo, Khu rung, Bi lac duong, Chia se) duoc goi y truyen 1.
--   Diem: nhan vat +3, noi chon +2, thu thach +1, chua doc xong +1 = 7.
-- Dieu uoc 4 (be 1: Meo Mun, Ngoi nha, Giup ban, Biet lang nghe) duoc goi y truyen Creator 16.
--   Diem: nhan vat +3, bai hoc +1, chua doc xong +1 = 5. Dung de storyImpact cua truyen 16 co pendingWishCount = 1.
INSERT INTO story_wish_suggestions (
    "Id", "StoryWishId", "StoryId", "CommunitySubmissionId", "Rank", "Score", "MatchedCardIds", "CreatedAt", "IsDeleted"
)
VALUES
    (1, 1, 1, NULL, 1, 7, '1,5,9', NOW() - INTERVAL '1 day', false),
    (2, 4, 16, NULL, 1, 5, '26,25', NOW(), false)
ON CONFLICT ("Id") DO NOTHING;
