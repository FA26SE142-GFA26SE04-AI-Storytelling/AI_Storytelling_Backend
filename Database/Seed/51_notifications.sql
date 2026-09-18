-- 51. notifications (20)
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
    (10, 7, 'ContentReported', '{"storyId":9}', 'Unread', NULL, NOW() - INTERVAL '15 days', false),
    (11, 2, 'PermissionRequestCreated', '{"permissionRequestId":1,"supervisionRelationshipId":11,"permissions":["ViewProgress","ViewResults"]}', 'Unread', NULL, NOW() - INTERVAL '3 days', false),
    (12, 7, 'PermissionRequestAccepted', '{"permissionRequestId":2,"permissions":["ViewProgress","ReceiveReport"]}', 'Read', NOW() - INTERVAL '2 days', NOW() - INTERVAL '2 days', false),
    (13, 2, 'PermissionRequestRejected', '{"permissionRequestId":3}', 'Unread', NULL, NOW() - INTERVAL '1 day', false),
    (14, 8, 'PermissionRequestRejected', '{"permissionRequestId":4}', 'Unread', NULL, NOW() - INTERVAL '18 hours', false),
    (15, 3, 'OwnershipTransferRequested', '{"ownershipTransferRequestId":1,"childProfileId":1,"requesterUserId":2,"responderUserId":3}', 'Unread', NULL, NOW() - INTERVAL '2 hours', false),
    (16, 2, 'OwnershipTransferRequested', '{"ownershipTransferRequestId":2,"childProfileId":2,"requesterUserId":7,"responderUserId":2}', 'Unread', NULL, NOW() - INTERVAL '3 hours', false),
    (17, 3, 'OwnershipTransferRejected', '{"ownershipTransferRequestId":3,"childProfileId":3}', 'Read', NOW() - INTERVAL '1 day', NOW() - INTERVAL '1 day', false),
    (18, 8, 'OwnershipTransferRejected', '{"ownershipTransferRequestId":4,"childProfileId":4}', 'Unread', NULL, NOW() - INTERVAL '12 hours', false),
    (19, 2, 'OwnershipTransferAccepted', '{"ownershipTransferRequestId":5,"childProfileId":11,"newOwnerUserId":3}', 'Read', NOW() - INTERVAL '6 hours', NOW() - INTERVAL '6 hours', false),
    (20, 8, 'OwnershipTransferAccepted', '{"ownershipTransferRequestId":6,"childProfileId":12,"newOwnerUserId":8}', 'Unread', NULL, NOW() - INTERVAL '3 hours', false)
ON CONFLICT ("Id") DO NOTHING;
