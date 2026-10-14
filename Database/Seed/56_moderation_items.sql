-- 56. moderation_items (16): queue examples for gray-zone stories, reports, roadmaps and Community.
-- PublicStoryReview is not seeded until the Public Library story model exists.
INSERT INTO moderation_items (
    "Id", "ItemType", "Status", "Priority", "StoryId", "StoryVersionId",
    "CreatedByUserId", "SourceChildProfileId", "ClaimedByUserId", "ClaimedAt",
    "SlaDueAt", "Flags", "Checklist", "ReviewProgress", "DecisionReason",
    "DecidedByUserId", "DecidedAt", "RowVersion", "IsResubmission", "CreatedAt", "IsDeleted"
)
VALUES
    (1, 'GrayZoneStory', 'Claimed', 'High', 4, 4,
     8, 4, 11, NOW() - INTERVAL '4 hours',
     NOW() + INTERVAL '8 hours', '{"sections":["development"]}'::jsonb,
     '{"safety":false,"ageBand":false}'::jsonb, '{"section":"development","completed":false}'::jsonb,
     NULL, NULL, NULL, 1, false, NOW() - INTERVAL '1 day', false),
    (2, 'GrayZoneStory', 'Cleared', 'Medium', 6, 6,
     9, 6, 12, NOW() - INTERVAL '20 days' + INTERVAL '1 hour',
     NOW() - INTERVAL '19 days', '{"sections":["ending"]}'::jsonb,
     '{"safety":true,"ageBand":true}'::jsonb, '{"section":"ending","completed":true}'::jsonb,
     'Da sua cach dien dat phu hop do tuoi', 12, NOW() - INTERVAL '20 days' + INTERVAL '90 minutes',
     2, false, NOW() - INTERVAL '20 days', false)
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
     'Can doi chieu nguon ban quyen', 11, NOW() - INTERVAL '4 hours',
     NOW() - INTERVAL '4 hours', NULL,
     2, false, NOW() - INTERVAL '6 hours', false),
    (8, 'ContentReport', 'Closed', 'Low', 10, 10,
     7, 12, 11, NOW() - INTERVAL '4 hours',
     NOW() + INTERVAL '19 hours', NULL, '{"cultural":true}'::jsonb,
     'Nhan va noi dung phu hop', NULL, 11, NOW() - INTERVAL '3 hours',
     NULL, 'Resolved',
     2, false, NOW() - INTERVAL '5 hours', false),
    (9, 'ContentReport', 'Archived', 'High', 9, 9,
     1, 7, 11, NOW() - INTERVAL '15 days' + INTERVAL '1 hour',
     NOW() - INTERVAL '14 days', NULL, '{"safety":true}'::jsonb,
     'Noi dung khong phu hop nen da luu tru truyen', NULL, 11, NOW() - INTERVAL '14 days',
     NULL, NULL,
     2, false, NOW() - INTERVAL '15 days', false),
    (10, 'ContentReport', 'Dismissed', 'Low', 4, 4,
     4, 10, 12, NOW() - INTERVAL '22 days' + INTERVAL '1 hour',
     NOW() - INTERVAL '21 days', NULL, '{"factCheck":true}'::jsonb,
     'Thong tin khoa hoc da duoc doi chieu', NULL, 12, NOW() - INTERVAL '20 days',
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
     '{"storyChecksCompleted":1,"storyCount":1}'::jsonb, 'Du dieu kien xuat ban',
     12, NOW() - INTERVAL '11 days', 2, false, NOW() - INTERVAL '12 days', false),
    (12, 'RoadmapTemplateReview', 'ChangesRequested', 'Medium', 2, 11,
     12, NOW() - INTERVAL '1 day', NOW() + INTERVAL '1 day',
     '{"storyChecksCompleted":1,"storyCount":1}'::jsonb, 'Can dieu chinh so ngay doc cho tung truyen',
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
     'Can xac nhan quyen chia se cong dong', 12, NOW() - INTERVAL '16 hours', NULL, NULL,
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
     'Khong cho phep dua noi dung len thu vien', 11, NOW() - INTERVAL '14 days', NULL, NULL,
     2, false, NOW() - INTERVAL '15 days', false)
ON CONFLICT ("Id") DO NOTHING;
