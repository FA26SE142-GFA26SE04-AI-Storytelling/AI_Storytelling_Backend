-- 24. story_generation_jobs (5 outline jobs + one completed legacy media tree; no runnable work)
INSERT INTO story_generation_jobs (
    "Id", "StoryId", "PromptCatalogVersionId", "GenerationRequestId", "StoryVersionId", "RequestedByUserId",
    "OperationKey", "Operation", "Stage", "Status", "AttemptNo", "MaxAttempts", "ConcurrencyToken",
    "StartedAt", "CompletedAt", "GuardrailResult", "GenerationMetadataJson", "CreatedAt", "IsDeleted")
VALUES
    (1, 2, 2, 1, 2, 7, 'seed-outline-2', 'GenerateOutline', 'OutlineGenerated', 'Completed', 1, 3, md5('seed-job-1'), NOW() - INTERVAL '28 days', NOW() - INTERVAL '28 days', 'Passed', '{"seeded":true}'::jsonb, NOW() - INTERVAL '28 days', false),
    (2, 4, 4, 2, 4, 8, 'seed-outline-4', 'GenerateOutline', 'OutlineGenerated', 'Completed', 1, 3, md5('seed-job-2'), NOW() - INTERVAL '24 days', NOW() - INTERVAL '24 days', 'Passed', '{"seeded":true}'::jsonb, NOW() - INTERVAL '24 days', false),
    (3, 6, 4, 3, 6, 9, 'seed-outline-6', 'GenerateOutline', 'OutlineGenerated', 'Completed', 1, 3, md5('seed-job-3'), NOW() - INTERVAL '20 days', NOW() - INTERVAL '20 days', 'Passed', '{"seeded":true}'::jsonb, NOW() - INTERVAL '20 days', false),
    (4, 8, 4, 4, 8, 10, 'seed-outline-8', 'GenerateOutline', 'OutlineGenerated', 'Completed', 1, 3, md5('seed-job-4'), NOW() - INTERVAL '17 days', NOW() - INTERVAL '17 days', 'Passed', '{"seeded":true}'::jsonb, NOW() - INTERVAL '17 days', false),
    (5, 10, 5, 5, 10, 7, 'seed-outline-10', 'GenerateOutline', 'OutlineGenerated', 'Completed', 1, 3, md5('seed-job-5'), NOW() - INTERVAL '13 days', NOW() - INTERVAL '13 days', 'Passed', '{"seeded":true}'::jsonb, NOW() - INTERVAL '13 days', false)
ON CONFLICT ("Id") DO NOTHING;

-- Historical media package for story 6. Revision 0 retains legacy confirmation semantics.
INSERT INTO story_generation_jobs (
    "Id", "StoryId", "StoryVersionId", "GenerationRequestId", "RequestedByUserId", "ParentJobId",
    "MediaContextId", "MediaPackageRevision", "Operation", "Stage", "Status", "AttemptNo", "MaxAttempts",
    "ConcurrencyToken", "StartedAt", "CompletedAt", "CreatedAt", "IsDeleted")
VALUES
    (6, 6, 6, 3, 9, NULL, 6, 0, 'GenerateMediaPackage', 'MediaCompleted', 'Completed', 1, 3, md5('seed-media-root-6'), NOW() - INTERVAL '20 days', NOW() - INTERVAL '20 days', NOW() - INTERVAL '20 days', false),
    (7, 6, 6, 3, 9, 6, 6, 0, 'GenerateIllustration', 'MediaCompleted', 'Completed', 1, 3, md5('seed-media-image-6'), NOW() - INTERVAL '20 days', NOW() - INTERVAL '20 days', NOW() - INTERVAL '20 days', false),
    (8, 6, 6, 3, 9, 6, 6, 0, 'GenerateTts', 'MediaCompleted', 'Completed', 1, 3, md5('seed-media-audio-6'), NOW() - INTERVAL '20 days', NOW() - INTERVAL '20 days', NOW() - INTERVAL '20 days', false)
ON CONFLICT ("Id") DO NOTHING;

UPDATE story_generation_requests AS request
SET "HandoffJobId" = mapping.job_id,
    "HandoffCreatedAt" = request."CreatedAt"
FROM (VALUES (1, 1), (2, 2), (3, 3), (4, 4), (5, 5)) AS mapping(request_id, job_id)
WHERE request."Id" = mapping.request_id
  AND request."HandoffJobId" IS NULL;
