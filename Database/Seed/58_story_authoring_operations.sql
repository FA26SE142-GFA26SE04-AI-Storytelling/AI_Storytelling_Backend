-- 58. API command receipts must be created by real requests, not fabricated by seed.
INSERT INTO story_authoring_operations (
    "Id", "StoryId", "StoryVersionId", "ActorUserId", "Kind", "OperationId", "RequestHash", "OperationResponseJson", "CreatedAt", "IsDeleted"
)
SELECT 0, 0, 0, 0, '', '', '', '', NOW(), false
WHERE false
ON CONFLICT ("Id") DO NOTHING;
