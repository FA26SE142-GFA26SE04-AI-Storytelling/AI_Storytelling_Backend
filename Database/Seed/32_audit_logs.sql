-- 32. audit_logs (18)
INSERT INTO audit_logs ("Id", "ActorUserId", "Action", "EntityType", "EntityId", "BeforeState", "AfterState", "OccurredAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 'CREATE_STORY', 'Story', 1, NULL, '{"status":"Draft"}', NOW() - INTERVAL '30 days', NOW() - INTERVAL '30 days', false),
    (2, 2, 'CREATE_CHILD_PROFILE', 'ChildProfile', 1, NULL, '{"status":"Active"}', NOW() - INTERVAL '40 days', NOW() - INTERVAL '40 days', false),
    (3, 7, 'ASSIGN_STORY', 'Assignment', 1, NULL, '{"status":"Assigned"}', NOW() - INTERVAL '15 days', NOW() - INTERVAL '15 days', false),
    (4, 3, 'UPDATE_STORY', 'Story', 3, '{"status":"ContentReview"}', '{"status":"Ready"}', NOW() - INTERVAL '26 days', NOW() - INTERVAL '26 days', false),
    (6, 4, 'REVIEW_RECOMMENDATION', 'Recommendation', 4, '{"status":"PendingReview"}', '{"status":"Applied"}', NOW() - INTERVAL '5 days', NOW() - INTERVAL '5 days', false),
    (10, 6, 'REVOKE_SUPERVISION', 'SupervisionRelationship', 9, '{"revoked":false}', '{"revoked":true}', NOW() - INTERVAL '20 days', NOW() - INTERVAL '20 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- Danh gia truyen (73_story_ratings.sql, 74_story_rating_reports.sql, muc 30-33): cung Action / EntityType / JSON
-- nhu StoryRatingService va StoryRatingModerationService ghi, de man audit cua Admin loc duoc theo StoryRating*.
INSERT INTO audit_logs ("Id", "ActorUserId", "Action", "EntityType", "EntityId", "BeforeState", "AfterState", "OccurredAt", "CreatedAt", "IsDeleted")
VALUES
    (11, 2, 'StoryRatingCreated', 'StoryRating', 1, NULL, '{"status":"Published","rating":5,"screening":"CLEAN","screeningVersion":"story-rating-rules-v1"}', NOW() - INTERVAL '40 hours', NOW() - INTERVAL '40 hours', false),
    (12, 3, 'StoryRatingCreated', 'StoryRating', 2, NULL, '{"status":"PendingModeration","rating":2,"screening":"LOW_RATING_WITH_TEXT","screeningVersion":"story-rating-rules-v1"}', NOW() - INTERVAL '20 hours', NOW() - INTERVAL '20 hours', false),
    (13, 2, 'StoryRatingCreated', 'StoryRating', 3, NULL, '{"status":"Published","rating":4,"screening":"CLEAN","screeningVersion":"story-rating-rules-v1"}', NOW() - INTERVAL '2 days', NOW() - INTERVAL '2 days', false),
    (14, 4, 'StoryRatingCreated', 'StoryRating', 4, NULL, '{"status":"PendingModeration","rating":1,"screening":"LOW_RATING_WITH_TEXT","screeningVersion":"story-rating-rules-v1"}', NOW() - INTERVAL '2 days', NOW() - INTERVAL '2 days', false),
    (15, 5, 'StoryRatingCreated', 'StoryRating', 5, NULL, '{"status":"Published","rating":5,"screening":"CLEAN","screeningVersion":"story-rating-rules-v1"}', NOW() - INTERVAL '30 hours', NOW() - INTERVAL '30 hours', false),
    (16, 3, 'StoryRatingCreated', 'StoryRating', 6, NULL, '{"status":"Published","rating":3,"screening":"CLEAN","screeningVersion":"story-rating-rules-v1"}', NOW() - INTERVAL '6 hours', NOW() - INTERVAL '6 hours', false),
    (17, 11, 'StoryRatingOpened', 'ModerationItem', 32, NULL, '{"ratingId":4}', NOW() - INTERVAL '25 hours', NOW() - INTERVAL '25 hours', false),
    (18, 11, 'StoryRatingHidden', 'ModerationItem', 32, '{"ratingStatus":"PendingModeration"}', '{"ratingStatus":"Hidden","ratingId":4,"reason":"Nhận xét công kích, không góp ý cụ thể cho truyện","reports":0}', NOW() - INTERVAL '1 day', NOW() - INTERVAL '1 day', false),
    (19, 3, 'StoryRatingReported', 'StoryRatingReport', 1, NULL, '{"ratingId":1,"itemId":31,"reason":"Irrelevant"}', NOW() - INTERVAL '10 hours', NOW() - INTERVAL '10 hours', false),
    (20, 4, 'StoryRatingReported', 'StoryRatingReport', 2, NULL, '{"ratingId":3,"itemId":33,"reason":"Other"}', NOW() - INTERVAL '8 hours', NOW() - INTERVAL '8 hours', false),
    (21, 12, 'StoryRatingOpened', 'ModerationItem', 33, NULL, '{"ratingId":3}', NOW() - INTERVAL '6 hours', NOW() - INTERVAL '6 hours', false),
    (22, 12, 'StoryRatingApproved', 'ModerationItem', 33, '{"ratingStatus":"Published"}', '{"ratingStatus":"Published","ratingId":3,"reason":"Đánh giá chỉ chấm sao, không vi phạm quy định","reports":1}', NOW() - INTERVAL '5 hours', NOW() - INTERVAL '5 hours', false)
ON CONFLICT ("Id") DO NOTHING;
