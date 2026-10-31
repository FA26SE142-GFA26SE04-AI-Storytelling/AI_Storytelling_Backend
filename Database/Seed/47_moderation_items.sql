-- 47. moderation_items (27): queue examples including the WF-L5A PublicStoryReview chain.
INSERT INTO moderation_items (
    "Id", "ItemType", "Status", "Priority", "StoryId", "StoryVersionId",
    "CreatedByUserId", "SourceChildProfileId", "ClaimedByUserId", "ClaimedAt",
    "SlaDueAt", "Flags", "Checklist", "ReviewProgress", "DecisionReason",
    "DecidedByUserId", "DecidedAt", "RowVersion", "IsResubmission", "CreatedAt", "IsDeleted"
)
VALUES
    (1, 'GrayZoneStory', 'Claimed', 'High', 4, 4,
     8, 4, 11, NOW() - INTERVAL '4 hours',
     NOW() + INTERVAL '8 hours', '{"jobId":null,"fingerprint":"5EED00000000000000000000000000000000000000000000000000000000A001","qualityPassed":true}'::jsonb,
     '{"safety":false,"ageBand":false}'::jsonb, '{"section":"development","completed":false}'::jsonb,
     NULL, NULL, NULL, 1, false, NOW() - INTERVAL '1 day', false),
    (2, 'GrayZoneStory', 'Cleared', 'Medium', 6, 6,
     9, 6, 12, NOW() - INTERVAL '20 days' + INTERVAL '1 hour',
     NOW() - INTERVAL '19 days', '{"sections":["ending"]}'::jsonb,
     '{"safety":true,"ageBand":true}'::jsonb, '{"section":"ending","completed":true}'::jsonb,
     'Đã sửa cách diễn đạt phù hợp độ tuổi', 12, NOW() - INTERVAL '20 days' + INTERVAL '90 minutes',
     2, false, NOW() - INTERVAL '20 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- WF-L5A Public Library review history.
-- Story 13 stops at ChangesRequested so creator_demo can submit changed version 16.
-- Story 14 already has a recent Pending resubmission (item 21), boosted for moderator_demo1 for 24 hours.
-- Story 16 was approved (item 22) and has no open item, so creator_demo can request its removal.
INSERT INTO moderation_items (
    "Id", "ItemType", "Status", "Priority", "StoryId", "StoryVersionId", "CreatedByUserId",
    "ClaimedByUserId", "ClaimedAt", "SlaDueAt", "Checklist", "ReviewProgress", "Feedback",
    "DecisionReason", "DecidedByUserId", "DecidedAt", "WithdrawnAt", "PreviousItemId",
    "RowVersion", "IsResubmission", "CreatedAt", "IsDeleted"
)
VALUES
    (17, 'PublicStoryReview', 'Approved', 'Medium', 13, 14, 13,
     12, NOW() - INTERVAL '6 days' - INTERVAL '30 minutes', NOW() - INTERVAL '5 days',
     '{"safety":true,"ageFit":true,"education":true,"language":true,"copyright":true,"image":true,"audio":true,"interaction":true}'::jsonb,
     '{"viewed":{"scene":[14],"audio":[14]}}'::jsonb,
     '{"general":"Đạt yêu cầu công khai"}'::jsonb, NULL,
     12, NOW() - INTERVAL '6 days', NULL, NULL,
     2, false, NOW() - INTERVAL '7 days', false),
    (18, 'PublicStoryReview', 'ChangesRequested', 'Medium', 13, 15, 13,
     11, NOW() - INTERVAL '8 hours', NOW() + INTERVAL '2 days',
     '{"safety":true,"ageFit":true,"education":false,"language":true,"copyright":true,"image":true,"audio":true,"interaction":true}'::jsonb,
     '{"viewed":{"scene":[15],"audio":[15]}}'::jsonb,
     '{"general":"Bổ sung việc xin phép và kết thúc tích cực","parts":[{"partType":"scene_text","partId":15,"note":"Làm rõ cách hai bạn sẻ chia"}]}'::jsonb,
     'Bổ sung việc xin phép và kết thúc tích cực',
     11, NOW() - INTERVAL '6 hours', NULL, 17,
     2, true, NOW() - INTERVAL '1 day', false),
    (19, 'PublicStoryReview', 'Approved', 'Medium', 14, 17, 13,
     12, NOW() - INTERVAL '7 days' - INTERVAL '30 minutes', NOW() - INTERVAL '6 days',
     '{"safety":true,"ageFit":true,"education":true,"language":true,"copyright":true,"image":true,"audio":true,"interaction":true}'::jsonb,
     '{"viewed":{"scene":[17],"audio":[17]}}'::jsonb,
     '{"general":"Đạt yêu cầu công khai"}'::jsonb, NULL,
     12, NOW() - INTERVAL '7 days', NULL, NULL,
     2, false, NOW() - INTERVAL '8 days', false),
    (20, 'PublicStoryReview', 'ChangesRequested', 'Medium', 14, 18, 13,
     11, NOW() - INTERVAL '8 hours', NOW() + INTERVAL '2 days',
     '{"safety":false,"ageFit":true,"education":true,"language":true,"copyright":true,"image":true,"audio":true,"interaction":true}'::jsonb,
     '{"viewed":{"scene":[18],"audio":[18]}}'::jsonb,
     '{"general":"Thay chi tiết lửa bằng nguồn sáng an toàn","parts":[{"partType":"scene_text","partId":18,"note":"Bỏ chi tiết gây nguy hiểm"}]}'::jsonb,
     'Thay chi tiết lửa bằng nguồn sáng an toàn',
     11, NOW() - INTERVAL '6 hours', NULL, 19,
     2, true, NOW() - INTERVAL '1 day', false),
    (21, 'PublicStoryReview', 'Pending', 'Medium', 14, 19, 13,
     NULL, NULL, NOW() + INTERVAL '71 hours', NULL, NULL, NULL, NULL,
     NULL, NULL, NULL, 20,
     0, true, NOW() - INTERVAL '1 hour', false),
    (22, 'PublicStoryReview', 'Approved', 'Medium', 16, 21, 13,
     12, NOW() - INTERVAL '5 days' - INTERVAL '30 minutes', NOW() - INTERVAL '4 days',
     '{"safety":true,"ageFit":true,"education":true,"language":true,"copyright":true,"image":true,"audio":true,"interaction":true}'::jsonb,
     '{"viewed":{"scene":[21],"audio":[21]}}'::jsonb,
     '{"general":"Đạt yêu cầu công khai"}'::jsonb, NULL,
     12, NOW() - INTERVAL '5 days', NULL, NULL,
     2, false, NOW() - INTERVAL '6 days', false),
    (23, 'PublicStoryReview', 'Approved', 'Medium', 17, 22, 13,
     12, NOW() - INTERVAL '9 days' - INTERVAL '30 minutes', NOW() - INTERVAL '8 days',
     '{"safety":true,"ageFit":true,"education":true,"language":true,"copyright":true,"image":true,"audio":true,"interaction":true}'::jsonb,
     '{"viewed":{"scene":[22],"audio":[22]}}'::jsonb,
     '{"general":"Đạt yêu cầu công khai"}'::jsonb, NULL,
     12, NOW() - INTERVAL '9 days', NULL, NULL,
     2, false, NOW() - INTERVAL '10 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- Story 17: creator removal request approved by moderator_demo2, so the approved version may be revised.
INSERT INTO moderation_items (
    "Id", "ItemType", "Status", "Priority", "StoryId", "StoryVersionId", "CreatedByUserId",
    "ClaimedByUserId", "ClaimedAt", "SlaDueAt", "Flags", "DecisionReason",
    "DecidedByUserId", "DecidedAt", "RemovedAt", "RowVersion", "IsResubmission", "CreatedAt", "IsDeleted"
)
VALUES
    (24, 'PublicStoryRemoval', 'Approved', 'Medium', 17, 22, 13,
     12, NOW() - INTERVAL '2 days' - INTERVAL '30 minutes', NOW() - INTERVAL '1 day',
     '{"requestReason":"Tôi muốn viết lại truyện này"}'::jsonb, 'Đồng ý theo yêu cầu của tác giả',
     12, NOW() - INTERVAL '2 days', NOW() - INTERVAL '2 days', 2, false, NOW() - INTERVAL '3 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- Story 18: first submission still Pending and unclaimed, reserved for creator_demo's withdraw test.
INSERT INTO moderation_items (
    "Id", "ItemType", "Status", "Priority", "StoryId", "StoryVersionId", "CreatedByUserId",
    "SlaDueAt", "RowVersion", "IsResubmission", "CreatedAt", "IsDeleted"
)
VALUES
    (26, 'PublicStoryReview', 'Pending', 'Medium', 18, 23, 13,
     NOW() + INTERVAL '71 hours', 0, false, NOW() - INTERVAL '30 minutes', false)
ON CONFLICT ("Id") DO NOTHING;

INSERT INTO moderation_items (
    "Id", "ItemType", "Status", "Priority", "StoryId", "StoryVersionId",
    "ContentReportId", "CreatedByUserId", "ClaimedByUserId", "ClaimedAt",
    "SlaDueAt", "SlaWarnedAt", "ReviewProgress", "DecisionReason", "EscalationReason",
    "DecidedByUserId", "DecidedAt", "EscalatedAt", "ClosedReason",
    "RowVersion", "IsResubmission", "CreatedAt", "IsDeleted"
)
VALUES
    (3, 'ContentReport', 'Pending', 'Medium', 2, 2,
     2, 8, NULL, NULL,
     NOW() - INTERVAL '12 days', NOW() - INTERVAL '12 days', NULL, NULL, NULL,
     NULL, NULL, NULL, NULL,
     0, false, NOW() - INTERVAL '13 days', false),
    (4, 'ContentReport', 'Escalated', 'High', 6, 6,
     6, 8, 11, NOW() - INTERVAL '5 hours',
     NOW() + INTERVAL '18 hours', NULL, '{"copyright":true}'::jsonb, NULL,
     'Cần đối chiếu nguồn bản quyền', 11, NOW() - INTERVAL '4 hours',
     NOW() - INTERVAL '4 hours', NULL,
     2, false, NOW() - INTERVAL '6 hours', false),
    (8, 'ContentReport', 'Closed', 'Low', 10, 10,
     7, 12, 11, NOW() - INTERVAL '4 hours',
     NOW() + INTERVAL '19 hours', NULL, '{"cultural":true}'::jsonb,
     'Nhãn và nội dung phù hợp', NULL, 11, NOW() - INTERVAL '3 hours',
     NULL, 'Resolved',
     2, false, NOW() - INTERVAL '5 hours', false),
    (9, 'ContentReport', 'Archived', 'High', 9, 9,
     1, 7, 11, NOW() - INTERVAL '15 days' + INTERVAL '1 hour',
     NOW() - INTERVAL '14 days', NULL, '{"safety":true}'::jsonb,
     'Nội dung không phù hợp nên đã lưu trữ truyện', NULL, 11, NOW() - INTERVAL '14 days',
     NULL, NULL,
     2, false, NOW() - INTERVAL '15 days', false),
    (10, 'ContentReport', 'Dismissed', 'Low', 4, 4,
     4, 10, 12, NOW() - INTERVAL '22 days' + INTERVAL '1 hour',
     NOW() - INTERVAL '21 days', NULL, '{"factCheck":true}'::jsonb,
     'Thông tin khoa học đã được đối chiếu', NULL, 12, NOW() - INTERVAL '20 days',
     NULL, NULL,
     2, false, NOW() - INTERVAL '22 days', false),
    (11, 'ContentReport', 'Pending', 'High', 8, 8,
     5, 7, NULL, NULL,
     NOW() - INTERVAL '4 days', NOW() - INTERVAL '4 days', NULL,
     NULL, NULL, NULL, NULL,
     NULL, NULL,
     0, false, NOW() - INTERVAL '5 days', false)
ON CONFLICT ("Id") DO NOTHING;

INSERT INTO moderation_items (
    "Id", "ItemType", "Status", "Priority", "ReadingRoadmapId", "CreatedByUserId",
    "ClaimedByUserId", "ClaimedAt", "SlaDueAt", "ReviewProgress", "DecisionReason",
    "DecidedByUserId", "DecidedAt", "RowVersion", "IsResubmission", "CreatedAt", "IsDeleted"
)
VALUES
    (5, 'RoadmapTemplateReview', 'Claimed', 'Medium', 3, 12,
     11, NOW() - INTERVAL '6 hours', NOW() + INTERVAL '18 hours',
     '{"storyChecksCompleted":1,"storyCount":2}'::jsonb, NULL,
     NULL, NULL, 1, false, NOW() - INTERVAL '1 day', false),
    (6, 'RoadmapTemplateReview', 'Approved', 'Medium', 1, 11,
     12, NOW() - INTERVAL '12 days' + INTERVAL '1 hour', NOW() - INTERVAL '10 days',
     '{"storyChecksCompleted":1,"storyCount":1}'::jsonb, 'Đủ điều kiện xuất bản',
     12, NOW() - INTERVAL '11 days', 2, false, NOW() - INTERVAL '12 days', false),
    (12, 'RoadmapTemplateReview', 'ChangesRequested', 'Medium', 2, 11,
     12, NOW() - INTERVAL '1 day', NOW() + INTERVAL '1 day',
     '{"storyChecksCompleted":1,"storyCount":1}'::jsonb, 'Cần điều chỉnh số ngày đọc cho từng truyện',
     12, NOW() - INTERVAL '6 hours', 2, false, NOW() - INTERVAL '2 days', false)
ON CONFLICT ("Id") DO NOTHING;

INSERT INTO moderation_items (
    "Id", "ItemType", "Status", "Priority", "StoryId", "StoryVersionId",
    "CreatedByUserId", "SourceChildProfileId", "CopyrightDeclaration", "PiiCheckResult",
    "SlaDueAt", "RowVersion", "IsResubmission", "CreatedAt", "IsDeleted"
)
VALUES
    (7, 'CommunitySubmission', 'Pending', 'Medium', 3, 3,
     3, 3, 'OwnWork', '{"passed":true}'::jsonb,
     NOW() + INTERVAL '1 day', 0, false, NOW() - INTERVAL '2 hours', false)
ON CONFLICT ("Id") DO NOTHING;

-- Mot ho so bi tu choi, sau do gui lai; ho so khac duoc rut truoc khi duyet.
INSERT INTO moderation_items (
    "Id", "ItemType", "Status", "Priority", "StoryId", "StoryVersionId",
    "CreatedByUserId", "SourceChildProfileId", "CopyrightDeclaration", "PiiCheckResult",
    "ClaimedByUserId", "ClaimedAt", "SlaDueAt", "Feedback", "DecisionReason",
    "DecidedByUserId", "DecidedAt", "WithdrawnAt", "PreviousItemId",
    "RowVersion", "IsResubmission", "CreatedAt", "IsDeleted"
)
VALUES
    (13, 'CommunitySubmission', 'Rejected', 'Medium', 5, 5,
     4, 5, 'FamilyUse', '{"passed":true}'::jsonb,
     12, NOW() - INTERVAL '20 hours', NOW() - INTERVAL '12 hours',
     '{"copyright":"Family-use declaration is insufficient for community sharing"}'::jsonb,
     'Cần xác nhận quyền chia sẻ cộng đồng', 12, NOW() - INTERVAL '16 hours', NULL, NULL,
     2, false, NOW() - INTERVAL '1 day', false),
    (14, 'CommunitySubmission', 'Pending', 'Medium', 5, 5,
     4, 5, 'OwnWork', '{"passed":true}'::jsonb,
     NULL, NULL, NOW() + INTERVAL '1 day',
     NULL, NULL, NULL, NULL, NULL, 13,
     0, true, NOW() - INTERVAL '3 hours', false),
    (15, 'CommunitySubmission', 'Withdrawn', 'Low', 8, 8,
     5, 8, 'OwnWork', '{"passed":true}'::jsonb,
     NULL, NULL, NOW() + INTERVAL '1 day',
     NULL, NULL, NULL, NULL, NOW() - INTERVAL '2 hours', NULL,
     1, false, NOW() - INTERVAL '8 hours', false),
    (16, 'CommunitySubmission', 'Blocked', 'High', 9, 9,
     6, 9, 'OwnWork', '{"passed":false,"reason":"safety_concern"}'::jsonb,
     11, NOW() - INTERVAL '14 days' + INTERVAL '1 hour', NOW() - INTERVAL '13 days',
     '{"safety":"Unsafe content for publication"}'::jsonb,
     'Không cho phép đưa nội dung lên thư viện', 11, NOW() - INTERVAL '14 days', NULL, NULL,
     2, false, NOW() - INTERVAL '15 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- Truyen 6 (AI, be 6) da duoc duyet len thu vien cong dong, danh rieng cho test go cua parent_demo3 (id 4, Owner be 6).
-- Muc 27: truyen 19 (AI, 6-8, nhan ANIMALS) cua be 11, do parent_demo2 (id 3, Owner be 11) nop, da duyet de be 6-8 tuoi
-- thay san trong thu vien cong dong. Khong dung cho test go de thu vien luon con du lieu.
INSERT INTO moderation_items (
    "Id", "ItemType", "Status", "Priority", "StoryId", "StoryVersionId",
    "CreatedByUserId", "SourceChildProfileId", "CopyrightDeclaration", "PiiCheckResult",
    "ClaimedByUserId", "ClaimedAt", "SlaDueAt", "Feedback",
    "DecidedByUserId", "DecidedAt", "RowVersion", "IsResubmission", "CreatedAt", "IsDeleted"
)
VALUES
    (25, 'CommunitySubmission', 'Approved', 'Medium', 6, 6,
     4, 6, 'AiGenerated', '{"passed":true}'::jsonb,
     11, NOW() - INTERVAL '3 days' - INTERVAL '30 minutes', NOW() - INTERVAL '2 days',
     '{"decision":"approved"}'::jsonb,
     11, NOW() - INTERVAL '3 days', 2, false, NOW() - INTERVAL '4 days', false),
    (27, 'CommunitySubmission', 'Approved', 'Medium', 19, 24,
     3, 11, 'AiGenerated', '{"passed":true}'::jsonb,
     12, NOW() - INTERVAL '2 days' - INTERVAL '30 minutes', NOW() - INTERVAL '1 day',
     '{"decision":"approved"}'::jsonb,
     12, NOW() - INTERVAL '2 days', 2, false, NOW() - INTERVAL '3 days', false)
ON CONFLICT ("Id") DO NOTHING;
