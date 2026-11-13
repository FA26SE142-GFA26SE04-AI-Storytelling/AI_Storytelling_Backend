-- 74. story_rating_reports (1): parent_demo2 (Id 3) báo cáo đánh giá 1, đang chờ ở mục 31.
INSERT INTO story_rating_reports (
    "Id", "StoryRatingId", "ReporterUserId", "Reason", "Note", "ModerationItemId", "Status", "ResolvedAt", "CreatedAt", "IsDeleted"
)
VALUES
    (1, 1, 3, 'Irrelevant', 'Nhận xét kể chuyện gia đình, không nói về nội dung truyện', 31, 'Open', NULL, NOW() - INTERVAL '10 hours', false)
ON CONFLICT ("Id") DO NOTHING;
