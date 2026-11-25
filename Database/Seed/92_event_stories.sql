-- 88. event_stories - demo fixtures for the event lifecycle.
INSERT INTO event_stories (
    "Id", "EventId", "StoryId", "StoryVersionId", "Source", "EventSubmissionId", "IsActive", "CreatedAt", "IsDeleted"
)
VALUES
    (1001, 1001, 16, 21, 'CreatorSubmission', 1001, true, NOW() - INTERVAL '7 days', false),
    (1002, 1001, 13, 16, 'ModeratorSeed', NULL, true, NOW() - INTERVAL '7 days', false)
ON CONFLICT ("Id") DO NOTHING;
