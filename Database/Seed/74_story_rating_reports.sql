-- 74. story_rating_reports (2):
-- Report 1: parent_demo2 (Id 3) báo cáo đánh giá 1, đang chờ ở mục 31.
-- Report 2: parent_demo3 (Id 4) báo cáo đánh giá 3, moderator_demo2 giữ đánh giá (mục 33 Approved).
INSERT INTO story_rating_reports (
    "Id", "StoryRatingId", "ReporterUserId", "Reason", "Note", "ModerationItemId", "Status", "ResolvedAt", "CreatedAt", "IsDeleted"
)
VALUES
    (1, 1, 3, 'Irrelevant', 'Nhận xét kể chuyện gia đình, không nói về nội dung truyện', 31, 'Open', NULL, NOW() - INTERVAL '10 hours', false),
    (2, 3, 4, 'Other', 'Chỉ chấm sao, không thấy góp ý gì cho truyện', 33, 'Approved', NOW() - INTERVAL '5 hours', NOW() - INTERVAL '8 hours', false)
ON CONFLICT ("Id") DO NOTHING;
