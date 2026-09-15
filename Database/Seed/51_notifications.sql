-- 51. notifications (10)
INSERT INTO notifications ("Id", "RecipientUserId", "Type", "Payload", "Status", "ReadAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 2, 'SupervisionInvite', '{"childProfileId":1}', 'Read', NOW() - INTERVAL '39 days', NOW() - INTERVAL '40 days', false),
    (2, 3, 'StoryShared', '{"storyId":3,"classGroupId":3}', 'Unread', NULL, NOW() - INTERVAL '13 days', false),
    (3, 4, 'HoldModeAlert', '{"childProfileId":5}', 'Read', NOW() - INTERVAL '3 days', NOW() - INTERVAL '4 days', false),
    (4, 7, 'AssignmentCancelled', '{"assignmentId":4}', 'Unread', NULL, NOW() - INTERVAL '12 days', false),
    (5, 4, 'RecommendationAwaitingReview', '{"recommendationId":1}', 'Read', NOW() - INTERVAL '7 days', NOW() - INTERVAL '8 days', false),
    (6, 7, 'OrgTeacherInvite', '{"organizationId":3}', 'Unread', NULL, NOW() - INTERVAL '19 days', false),
    (7, 5, 'OrgConsentRequest', '{"childProfileId":8,"organizationId":2}', 'Unread', NULL, NOW() - INTERVAL '33 days', false),
    (8, 6, 'DataRequestSlaWarning', '{"dataRequestId":9}', 'Read', NOW() - INTERVAL '6 days', NOW() - INTERVAL '6 days', false),
    (9, 1, 'AdminRoleChanged', '{"userId":1}', 'Read', NOW() - INTERVAL '58 days', NOW() - INTERVAL '58 days', false),
    (10, 7, 'ContentReported', '{"storyId":9}', 'Unread', NULL, NOW() - INTERVAL '15 days', false)
ON CONFLICT ("Id") DO NOTHING;
