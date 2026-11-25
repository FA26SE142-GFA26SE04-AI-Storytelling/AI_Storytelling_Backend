-- 94. event_votes - demo fixtures for the event lifecycle.
INSERT INTO event_votes (
    "Id", "EventId", "EventSubmissionId", "StoryId", "StoryVersionId", "ParticipationId", "ParentUserId", "ChildProfileId", "ReadingSessionId", "Stars", "IsDisqualified", "CreatedAt", "IsDeleted"
)
VALUES
    (1001, 1001, 1001, 16, 21, 1003, 2, 1, 13, 5, false, NOW() - INTERVAL '71 hours', false)
ON CONFLICT ("Id") DO NOTHING;
