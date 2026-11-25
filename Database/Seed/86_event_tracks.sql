-- 82. event_tracks - demo fixtures for the event lifecycle.
INSERT INTO event_tracks (
    "Id", "EventId", "TrackType", "RegistrationStartAt", "RegistrationEndAt", "ExecutionStartAt", "ExecutionEndAt", "MaxSubmissionsPerUser", "GrantAiCredits", "GrantSubmitCredits", "WaiveWeeklyLibraryLimit", "ClaimWindowHours", "MaxReallocationRounds", "AwardAllMissionCompleters", "CreatedAt", "IsDeleted"
)
VALUES
    (1001, 1002, 'Creator', NOW(), NOW() + INTERVAL '5 days', NOW(), NOW() + INTERVAL '7 days', 1, 0, 0, false, 72, 1, false, NOW() - INTERVAL '1 days', false)
ON CONFLICT ("Id") DO NOTHING;
