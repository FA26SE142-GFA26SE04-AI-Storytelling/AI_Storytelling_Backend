-- 73. story_wish_suggestions (1): dieu uoc 1 (be 1: Chu meo, Khu rung, Bi lac duong, Chia se) duoc goi y truyen 1.
-- Diem: nhan vat +3, noi chon +2, thu thach +1, chua doc xong +1 = 7.
INSERT INTO story_wish_suggestions (
    "Id", "StoryWishId", "StoryId", "CommunitySubmissionId", "Rank", "Score", "MatchedCardIds", "CreatedAt", "IsDeleted"
)
VALUES
    (1, 1, 1, NULL, 1, 7, '1,5,9', NOW() - INTERVAL '1 day', false)
ON CONFLICT ("Id") DO NOTHING;
