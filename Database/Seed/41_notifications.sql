-- 41. notifications (23)
INSERT INTO notifications ("Id", "RecipientUserId", "Type", "Payload", "Status", "ReadAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 2, 'SupervisionInvite', '{"childProfileId":1}', 'Read', NOW() - INTERVAL '39 days', NOW() - INTERVAL '40 days', false),
    (3, 4, 'HoldModeAlert', '{"childProfileId":5}', 'Read', NOW() - INTERVAL '3 days', NOW() - INTERVAL '4 days', false),
    (5, 4, 'RecommendationAwaitingReview', '{"recommendationId":1}', 'Read', NOW() - INTERVAL '7 days', NOW() - INTERVAL '8 days', false),
    (8, 6, 'DataRequestSlaWarning', '{"dataRequestId":9}', 'Read', NOW() - INTERVAL '6 days', NOW() - INTERVAL '6 days', false),
    (9, 1, 'AdminRoleChanged', '{"userId":1}', 'Read', NOW() - INTERVAL '58 days', NOW() - INTERVAL '58 days', false),
    (10, 7, 'ContentReported', '{"storyId":9}', 'Unread', NULL, NOW() - INTERVAL '15 days', false),
    (11, 2, 'PermissionRequest', '{"permissionRequestId":1,"supervisionRelationshipId":11,"permissions":["ViewProgress","ViewResults"]}', 'Unread', NULL, NOW() - INTERVAL '3 days', false),
    (12, 7, 'PermissionRequestResult', '{"permissionRequestId":2,"permissions":["ViewProgress","ReceiveReport"],"result":"accepted"}', 'Read', NOW() - INTERVAL '2 days', NOW() - INTERVAL '2 days', false),
    (13, 2, 'PermissionRequestResult', '{"permissionRequestId":3,"result":"rejected"}', 'Unread', NULL, NOW() - INTERVAL '1 day', false),
    (14, 8, 'PermissionRequestResult', '{"permissionRequestId":4,"result":"rejected"}', 'Unread', NULL, NOW() - INTERVAL '18 hours', false),
    (15, 3, 'OwnershipTransferRequest', '{"ownershipTransferRequestId":1,"childProfileId":1,"requesterUserId":2,"responderUserId":3}', 'Unread', NULL, NOW() - INTERVAL '2 hours', false),
    (16, 2, 'OwnershipTransferRequest', '{"ownershipTransferRequestId":2,"childProfileId":2,"requesterUserId":7,"responderUserId":2}', 'Unread', NULL, NOW() - INTERVAL '3 hours', false),
    (17, 3, 'OwnershipTransferResult', '{"ownershipTransferRequestId":3,"childProfileId":3,"result":"rejected"}', 'Read', NOW() - INTERVAL '1 day', NOW() - INTERVAL '1 day', false),
    (18, 8, 'OwnershipTransferResult', '{"ownershipTransferRequestId":4,"childProfileId":4,"result":"rejected"}', 'Unread', NULL, NOW() - INTERVAL '12 hours', false),
    (19, 2, 'OwnershipTransferResult', '{"ownershipTransferRequestId":5,"childProfileId":11,"newOwnerUserId":3,"result":"accepted"}', 'Read', NOW() - INTERVAL '6 hours', NOW() - INTERVAL '6 hours', false),
    (20, 8, 'OwnershipTransferResult', '{"ownershipTransferRequestId":6,"childProfileId":12,"newOwnerUserId":8,"result":"accepted"}', 'Unread', NULL, NOW() - INTERVAL '3 hours', false),
    (21, 1, 'ModerationSlaWarning', '{"contentReportId":2,"storyId":2}', 'Unread', NULL, NOW() - INTERVAL '2 hours', false),
    (22, 7, 'ContentReportResolved', '{"contentReportId":1,"storyId":9,"resolution":"ReviewedArchived"}', 'Read', NOW() - INTERVAL '13 days', NOW() - INTERVAL '14 days', false),
    (23, 1, 'ContentReportEscalated', '{"contentReportId":6,"storyId":6}', 'Unread', NULL, NOW() - INTERVAL '4 hours', false),
    (24, 7, 'ContentModerationResult', '{"contentReportId":1,"result":"ReviewedArchived"}', 'Read', NOW() - INTERVAL '13 days', NOW() - INTERVAL '14 days', false),
    (25, 9, 'StoryModerationResult', '{"storyId":6,"storyVersionId":6,"result":"Cleared"}', 'Read', NOW() - INTERVAL '19 days', NOW() - INTERVAL '20 days' + INTERVAL '90 minutes', false),
    (26, 6, 'StoryArchivedByModeration', '{"storyId":9,"reason":"SafetyConcern"}', 'Read', NOW() - INTERVAL '13 days', NOW() - INTERVAL '14 days', false),
    (27, 13, 'StaffInvite', '{"userId":13,"role":"ContentCreator"}', 'Read', NOW() - INTERVAL '34 days', NOW() - INTERVAL '35 days', false)
ON CONFLICT ("Id") DO NOTHING;
