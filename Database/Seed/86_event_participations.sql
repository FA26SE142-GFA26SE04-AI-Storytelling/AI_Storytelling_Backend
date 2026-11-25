-- 86. event_participations - demo fixtures for the event lifecycle.
INSERT INTO event_participations (
    "Id", "EventId", "EventActivityId", "UserId", "AcceptedRuleVersionId", "Status", "MissionCompletedAt", "VerifiedAt", "CreatedAt", "IsDeleted"
)
VALUES
    (1001, 1001, 1001, 13, 1001, 'Verified', NOW() - INTERVAL '7 days', NOW() - INTERVAL '2 days', NOW() - INTERVAL '10 days', false),
    (1002, 1001, 1002, 2, 1001, 'Verified', NOW() - INTERVAL '4 days', NOW() - INTERVAL '2 days', NOW() - INTERVAL '6 days', false),
    (1003, 1001, 1003, 2, 1001, 'Verified', NOW() - INTERVAL '3 days', NOW() - INTERVAL '2 days', NOW() - INTERVAL '6 days', false),
    (1004, 1001, 1002, 3, 1001, 'VerificationFailed', NULL, NOW() - INTERVAL '2 days', NOW() - INTERVAL '6 days', false)
ON CONFLICT ("Id") DO NOTHING;
