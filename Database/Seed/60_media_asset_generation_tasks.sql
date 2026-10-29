-- 60. One completed illustration task demonstrating durable history; workers never regenerate it.
-- Matches completed image branch 7, story/version/context 6 and existing Ready asset 5 / beat 6.
INSERT INTO media_asset_generation_tasks (
    "Id", "StoryId", "StoryVersionId", "BranchJobId", "MediaAssetId", "IllustrationBeatId", "MediaContextId",
    "PackageRevision", "Status", "ConcurrencyToken", "AttemptNo", "QueuedAt", "StartedAt", "CompletedAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 6, 6, 7, 5, 6, 6, 0, 'Completed', md5('seed-image-task-6-5'), 1,
     NOW() - INTERVAL '20 days', NOW() - INTERVAL '20 days', NOW() - INTERVAL '20 days', NOW() - INTERVAL '20 days', false)
ON CONFLICT DO NOTHING;
