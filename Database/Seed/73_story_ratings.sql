-- 73. story_ratings (4): đánh giá của phụ huynh cho truyện Creator đang công khai (truyện 16, 21).
-- Rating 1: parent_demo1 (Id 2) 5 sao truyện 21, đang hiện, đã bị parent_demo2 báo cáo (report 1, mục 31).
-- Rating 2: parent_demo2 (Id 3) 2 sao kèm chữ truyện 21, chờ Moderator (mục 30).
-- Rating 3: parent_demo1 4 sao không kèm chữ truyện 16, đang hiện.
-- Rating 4: parent_demo3 (Id 4) 1 sao truyện 16, moderator_demo1 đã ẩn (mục 32).
-- Phiên đọc làm căn cứ được đánh giá: reading_sessions 11–13 (24_reading_sessions.sql).
INSERT INTO story_ratings (
    "Id", "StoryId", "ReviewerUserId", "Rating", "Comment", "Status", "HiddenReason",
    "ModeratedByUserId", "ModeratedAt", "CreatedAt", "UpdatedAt", "IsDeleted"
)
VALUES
    (1, 21, 2, 5, 'Bé nhà mình rất thích cá heo, đọc xong còn kể lại cho cả nhà.', 'Published', NULL,
     NULL, NULL, NOW() - INTERVAL '40 hours', NULL, false),
    (2, 21, 3, 2, 'Kết truyện hơi vội, bé chưa hiểu vì sao rùa được thả.', 'PendingModeration', NULL,
     NULL, NULL, NOW() - INTERVAL '20 hours', NULL, false),
    (3, 16, 2, 4, NULL, 'Published', NULL,
     NULL, NULL, NOW() - INTERVAL '2 days', NULL, false),
    (4, 16, 4, 1, 'Truyện chán, đừng cho con đọc.', 'Hidden', 'Nhận xét công kích, không góp ý cụ thể cho truyện',
     11, NOW() - INTERVAL '1 day', NOW() - INTERVAL '2 days', NULL, false)
ON CONFLICT ("Id") DO NOTHING;

-- Gắn các mục hàng đợi đã chèn ở 42_moderation_items.sql (StoryRatingId = NULL) vào đánh giá tương ứng.
UPDATE moderation_items SET "StoryRatingId" = 2 WHERE "Id" = 30 AND "StoryRatingId" IS NULL;
UPDATE moderation_items SET "StoryRatingId" = 1 WHERE "Id" = 31 AND "StoryRatingId" IS NULL;
UPDATE moderation_items SET "StoryRatingId" = 4 WHERE "Id" = 32 AND "StoryRatingId" IS NULL;
