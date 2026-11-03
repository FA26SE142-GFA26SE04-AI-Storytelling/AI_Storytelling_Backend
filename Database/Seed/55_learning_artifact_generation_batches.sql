-- Runtime AI batches contain pinned request/context and real provider results.
-- Keep this operational table empty in demo data, like story_authoring_operations.
-- Do not fabricate completed AI work or enqueue paid provider calls during seed.
INSERT INTO learning_artifact_generation_batches (
    "Id", "StoryId", "StoryVersionId", "RootJobId", "RequestedByUserId",
    "SourceFingerprint", "ContextFingerprint", "Kind", "BatchNumber", "TargetCount",
    "RequestJson", "Status", "ConcurrencyToken", "AttemptCount", "CreatedAt", "IsDeleted"
)
SELECT 0, 0, 0, 0, 0, '', '', 'vocabulary', 0, 12, '{}'::jsonb, 'Pending', '', 0, NOW(), false
WHERE false
ON CONFLICT ("Id") DO NOTHING;
