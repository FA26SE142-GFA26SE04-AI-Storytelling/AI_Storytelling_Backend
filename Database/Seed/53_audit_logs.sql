-- 53. audit_logs (10)
INSERT INTO audit_logs ("Id", "ActorUserId", "Action", "EntityType", "EntityId", "BeforeState", "AfterState", "OccurredAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 'CREATE_STORY', 'Story', 1, NULL, '{"status":"Draft"}', NOW() - INTERVAL '30 days', NOW() - INTERVAL '30 days', false),
    (2, 2, 'CREATE_CHILD_PROFILE', 'ChildProfile', 1, NULL, '{"status":"Active"}', NOW() - INTERVAL '40 days', NOW() - INTERVAL '40 days', false),
    (3, 7, 'ASSIGN_STORY', 'Assignment', 1, NULL, '{"status":"Assigned"}', NOW() - INTERVAL '15 days', NOW() - INTERVAL '15 days', false),
    (4, 3, 'UPDATE_STORY', 'Story', 3, '{"status":"ContentReview"}', '{"status":"Ready"}', NOW() - INTERVAL '26 days', NOW() - INTERVAL '26 days', false),
    (5, 1, 'VERIFY_ORGANIZATION', 'Organization', 1, '{"verificationStatus":"PendingVerification"}', '{"verificationStatus":"Active"}', NOW() - INTERVAL '58 days', NOW() - INTERVAL '58 days', false),
    (6, 4, 'REVIEW_RECOMMENDATION', 'Recommendation', 4, '{"status":"PendingReview"}', '{"status":"Applied"}', NOW() - INTERVAL '5 days', NOW() - INTERVAL '5 days', false),
    (7, 9, 'CREATE_CLASS_GROUP', 'ClassGroup', 3, NULL, '{"status":"Active"}', NOW() - INTERVAL '36 days', NOW() - INTERVAL '36 days', false),
    (8, 8, 'SHARE_STORY', 'SharedStory', 2, NULL, '{"teacherStatus":"Pending"}', NOW() - INTERVAL '14 days', NOW() - INTERVAL '14 days', false),
    (9, 5, 'REQUEST_DATA_EXPORT', 'DataRequest', 8, NULL, '{"status":"Pending"}', NOW() - INTERVAL '5 days', NOW() - INTERVAL '5 days', false),
    (10, 6, 'REVOKE_SUPERVISION', 'SupervisionRelationship', 9, '{"revoked":false}', '{"revoked":true}', NOW() - INTERVAL '20 days', NOW() - INTERVAL '20 days', false)
ON CONFLICT ("Id") DO NOTHING;
