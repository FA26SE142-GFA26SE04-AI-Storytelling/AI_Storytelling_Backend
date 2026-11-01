-- 55. story_version_moderation_states: one row per seeded content version.
-- Version 19 (item 21) carries two review flags (pii-1, safety-1) so flags/resolve can be tested.
INSERT INTO story_version_moderation_states (
    "StoryVersionId", "ModerationZone", "GrayZoneFlags", "GrayZoneCleared", "GrayZoneClearedByUserId", "GrayZoneClearedAt", "ReviewStatus", "ReviewSubmittedAt", "ReviewedByUserId", "ReviewedAt", "ReviewFeedback"
)
VALUES
    (1, NULL, NULL, false, NULL, NULL, 'None', NULL, NULL, NULL, NULL),
    (2, NULL, NULL, false, NULL, NULL, 'None', NULL, NULL, NULL, NULL),
    (3, NULL, NULL, false, NULL, NULL, 'None', NULL, NULL, NULL, NULL),
    (4, NULL, NULL, false, NULL, NULL, 'None', NULL, NULL, NULL, NULL),
    (5, NULL, NULL, false, NULL, NULL, 'None', NULL, NULL, NULL, NULL),
    (6, NULL, NULL, false, NULL, NULL, 'None', NULL, NULL, NULL, NULL),
    (7, NULL, NULL, false, NULL, NULL, 'None', NULL, NULL, NULL, NULL),
    (8, NULL, NULL, false, NULL, NULL, 'None', NULL, NULL, NULL, NULL),
    (9, NULL, NULL, false, NULL, NULL, 'None', NULL, NULL, NULL, NULL),
    (10, NULL, NULL, false, NULL, NULL, 'None', NULL, NULL, NULL, NULL),
    (11, NULL, NULL, false, NULL, NULL, 'None', NULL, NULL, NULL, NULL),
    (12, NULL, NULL, false, NULL, NULL, 'None', NULL, NULL, NULL, NULL),
    (13, NULL, NULL, false, NULL, NULL, 'None', NULL, NULL, NULL, NULL),
    (14, NULL, NULL, false, NULL, NULL, 'Approved', NOW() - INTERVAL '7 days', 12, NOW() - INTERVAL '6 days', '{"general":"Đạt yêu cầu công khai"}'::jsonb),
    (15, NULL, NULL, false, NULL, NULL, 'ChangesRequested', NOW() - INTERVAL '2 days', 11, NOW() - INTERVAL '6 hours', '{"general":"Bổ sung việc xin phép và kết thúc tích cực"}'::jsonb),
    (16, NULL, NULL, false, NULL, NULL, 'None', NULL, NULL, NULL, NULL),
    (17, NULL, NULL, false, NULL, NULL, 'Approved', NOW() - INTERVAL '8 days', 12, NOW() - INTERVAL '7 days', '{"general":"Đạt yêu cầu công khai"}'::jsonb),
    (18, NULL, NULL, false, NULL, NULL, 'ChangesRequested', NOW() - INTERVAL '1 day', 11, NOW() - INTERVAL '6 hours', '{"general":"Thay chi tiết lửa bằng nguồn sáng an toàn"}'::jsonb),
    (19, NULL, NULL, false, NULL, NULL, 'Pending', NOW() - INTERVAL '1 hour', NULL, NULL, NULL),
    (20, NULL, NULL, false, NULL, NULL, 'None', NULL, NULL, NULL, NULL),
    (21, NULL, NULL, false, NULL, NULL, 'Approved', NOW() - INTERVAL '6 days', 12, NOW() - INTERVAL '5 days', '{"general":"Đạt yêu cầu công khai"}'::jsonb),
    (22, NULL, NULL, false, NULL, NULL, 'Approved', NOW() - INTERVAL '10 days', 12, NOW() - INTERVAL '9 days', '{"general":"Đạt yêu cầu công khai"}'::jsonb),
    (23, NULL, NULL, false, NULL, NULL, 'Pending', NOW() - INTERVAL '30 minutes', NULL, NULL, NULL),
    (24, NULL, NULL, false, NULL, NULL, 'None', NULL, NULL, NULL, NULL)
ON CONFLICT ("StoryVersionId") DO NOTHING;

UPDATE story_version_moderation_states
SET "ReviewStatus" = 'None', "ReviewSubmittedAt" = NULL,
    "ReviewedByUserId" = NULL, "ReviewedAt" = NULL, "ReviewFeedback" = NULL
WHERE "StoryVersionId" = 12;

UPDATE story_version_moderation_states
SET "ReviewStatus" = 'Approved', "ReviewSubmittedAt" = NOW() - INTERVAL '30 days' + INTERVAL '1 hour',
    "ReviewedByUserId" = 11, "ReviewedAt" = NOW() - INTERVAL '30 days' + INTERVAL '90 minutes'
WHERE "StoryVersionId" = 1 AND "ReviewStatus" = 'None';

-- Version 4 (item 1): same shape GrayZoneWorkflowService.RecordAsync writes, so the
-- fingerprint matches item 1's checkpoint and gray-zone Clear / Block can succeed.
UPDATE story_version_moderation_states
SET "ModerationZone" = 'GrayZone', "GrayZoneFlags" = '{"assessment":{"zone":2,"fingerprint":"5EED00000000000000000000000000000000000000000000000000000000A001","phase":"phase1","policyVersion":"seed","provider":"seed","model":"seed","riskScore":55,"findings":[{"kind":"paragraph","name":"paragraph:0","category":"GZ03","reason":"Bé tự du hành một mình lên vũ trụ, cần người lớn xem lại cho nhóm 9-10 tuổi","startOffset":0,"endOffset":53,"evidenceQuote":"bạn nhỏ quyết định du hành lên vũ trụ","contextNote":"Chưa có người lớn đi cùng"}],"ageBand":"9-10","language":"vi"},"flags":[{"id":"5EED000000000000:0","kind":"paragraph","name":"paragraph:0","reason":"Bé tự du hành một mình lên vũ trụ, cần người lớn xem lại cho nhóm 9-10 tuổi","category":"GZ03","evidenceQuote":"bạn nhỏ quyết định du hành lên vũ trụ","contextNote":"Chưa có người lớn đi cùng","startOffset":0,"endOffset":53}]}'::jsonb,
    "ReviewStatus" = 'Pending', "ReviewSubmittedAt" = NOW() - INTERVAL '1 day'
WHERE "StoryVersionId" = 4 AND "ReviewStatus" = 'None';

UPDATE story_version_moderation_states
SET "ModerationZone" = 'GrayZone', "GrayZoneFlags" = '{"sections":["ending"],"reason":"safety_review"}'::jsonb,
    "GrayZoneCleared" = true, "GrayZoneClearedByUserId" = 12,
    "GrayZoneClearedAt" = NOW() - INTERVAL '20 days' + INTERVAL '90 minutes',
    "ReviewStatus" = 'Approved', "ReviewSubmittedAt" = NOW() - INTERVAL '20 days' + INTERVAL '1 hour',
    "ReviewedByUserId" = 12, "ReviewedAt" = NOW() - INTERVAL '20 days' + INTERVAL '90 minutes',
    "ReviewFeedback" = '{"ending":"Adjusted wording for age group"}'::jsonb
WHERE "StoryVersionId" = 6 AND "ReviewStatus" = 'None';

-- Applied as an UPDATE so re-running the seed also adds the flags to an existing database.
UPDATE story_version_moderation_states
SET "GrayZoneFlags" = '{"flags":[{"id":"pii-1","kind":"pii","name":"An","reason":"Tên riêng có thể là thông tin cá nhân của trẻ","startOffset":0,"endOffset":2,"evidenceQuote":"An","category":"safety"},{"id":"safety-1","kind":"safety","name":"Ngọn đèn","reason":"Kiểm tra lại chi tiết dùng đèn sau lần yêu cầu sửa","startOffset":7,"endOffset":23,"evidenceQuote":"ngọn đèn an toàn","category":"safety"}]}'::jsonb
WHERE "StoryVersionId" = 19 AND "GrayZoneFlags" IS NULL;

UPDATE story_version_moderation_states
SET "ReviewStatus" = 'Rejected', "ReviewedByUserId" = 11,
    "ReviewedAt" = NOW() - INTERVAL '14 days',
    "ReviewFeedback" = '{"development":"Safety concern in this section"}'::jsonb
WHERE "StoryVersionId" = 9 AND "ReviewStatus" = 'None';
