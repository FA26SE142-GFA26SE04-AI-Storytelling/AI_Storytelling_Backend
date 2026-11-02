-- 67. story_summaries (1)
-- Tom tat cuoi truyen tre gui chu ho so. Seed chi co danh co (khong ghi am) de khong tro toi file am thanh
-- khong ton tai trong kho luu tru. Phien 1: tre 1, truyen 1, phien ban 1, da Completed.
INSERT INTO story_summaries (
    "Id", "ReadingSessionId", "ChildProfileId", "StoryId", "StoryVersionId",
    "AudioStoragePath", "AudioContentType", "AudioBytes", "RecordingSentAt", "FlagsSentAt",
    "CreatedAt", "IsDeleted"
)
SELECT
    1, s."Id", s."ChildProfileId", s."StoryId", s."StoryVersionId",
    NULL, NULL, NULL, NULL, s."CompletedAt" + INTERVAL '1 minute',
    s."CompletedAt" + INTERVAL '1 minute', false
FROM reading_sessions AS s
WHERE s."Id" = 1 AND s."Status" = 'Completed' AND s."CompletedAt" IS NOT NULL
ON CONFLICT ("Id") DO NOTHING;
