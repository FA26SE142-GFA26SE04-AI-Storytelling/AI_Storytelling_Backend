-- 87. event_submissions - demo fixtures for the event lifecycle.
INSERT INTO event_submissions (
    "Id", "EventId", "ParticipationId", "EventActivityId", "CreatorUserId", "StoryId", "CurrentStoryVersionId", "LockedStoryVersionId", "Status", "SubmittedAt", "CreatedAt", "IsDeleted"
)
VALUES
    (1001, 1001, 1001, 1001, 13, 16, 21, 21, 'Approved', NOW() - INTERVAL '9 days', NOW() - INTERVAL '9 days', false)
ON CONFLICT ("Id") DO NOTHING;
