-- 83. event_activities - demo fixtures for the event lifecycle.
INSERT INTO event_activities (
    "Id", "EventId", "ActivityType", "AllowedAudience", "RegistrationStartAt", "RegistrationEndAt", "StartAt", "EndAt", "ReviewCutoffAt", "GrantAiCredits", "WaiveWeeklyLibraryLimit", "StorySource", "MinVotesPerSubmission", "ClaimWindowHours", "MaxReallocationRounds", "AwardAllMissionCompleters", "CreatedAt", "IsDeleted"
)
VALUES
    (1001, 1001, 'Submission', 'Creator', NOW() - INTERVAL '11 days', NOW() - INTERVAL '8 days', NOW() - INTERVAL '11 days', NOW() - INTERVAL '8 days', NOW() - INTERVAL '7 days', 0, false, 'Both', 1, 72, 1, false, NOW() - INTERVAL '12 days', false),
    (1002, 1001, 'ReadingMissions', 'Parent', NOW() - INTERVAL '7 days', NOW() - INTERVAL '3 days', NOW() - INTERVAL '7 days', NOW() - INTERVAL '2 days', NULL, 0, true, 'CuratedOnly', 1, 72, 1, false, NOW() - INTERVAL '12 days', false),
    (1003, 1001, 'Voting', 'Parent', NOW() - INTERVAL '7 days', NOW() - INTERVAL '3 days', NOW() - INTERVAL '6 days', NOW() - INTERVAL '2 days', NULL, 0, false, 'Both', 1, 72, 1, false, NOW() - INTERVAL '12 days', false)
ON CONFLICT ("Id") DO NOTHING;
