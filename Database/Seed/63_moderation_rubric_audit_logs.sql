-- Synthetic import event for the demo Draft rubric; no story content or provider response.
INSERT INTO moderation_rubric_audit_logs (
    "Id", "RubricVersionId", "ActorUserId", "Action", "BeforeJson", "AfterJson", "CreatedAt", "UpdatedAt", "IsDeleted"
)
VALUES
    (1, 1, 1, 'Imported', NULL, '{"source":"demo-fixture","status":"Draft"}', NOW(), NULL, false),
    (2, 2, 1, 'Activated', NULL, '{"source":"demo-fixture","status":"Active"}', NOW() - INTERVAL '1 day', NULL, false)
ON CONFLICT DO NOTHING;
