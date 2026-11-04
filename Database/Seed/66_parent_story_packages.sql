-- Inert historical fixture only. Requires AddParentStoryPackages; this is NOT a safety approval or child grant.
-- Cancelled packages cannot generate, confirm, or be delivered. Create a fresh package through the API to evaluate current policy.
INSERT INTO parent_story_packages (
    "Id", "StoryId", "StoryVersionId", "ChildProfileId", "CreatedByUserId", "Revision", "ConcurrencyToken", "Status",
    "SourceFingerprint", "ContextFingerprint", "ContextSnapshotJson", "ComponentsJson", "CheckResultJson", "GrayZoneAssessmentJson", "ManifestJson",
    "CreatedAt", "IsDeleted"
)
VALUES (
    1, 15, 20, 1, 2, 1, '00000000000000000000000000000001', 'Cancelled',
    '0000000000000000000000000000000000000000000000000000000000000000',
    '0000000000000000000000000000000000000000000000000000000000000000',
    '{}', '{"vocabulary":false,"quiz":false,"listening":false,"images":false,"narration":false}',
    '{"passed":false,"errors":["HISTORICAL_CANCELLED_FIXTURE"]}', '{}',
    '{"vocabularyIds":[],"quizIds":[],"listeningIds":[],"sceneIds":[],"segmentIds":[],"mediaAssetIds":[]}',
    '2026-11-03T00:00:00Z', false
)
ON CONFLICT ("Id") DO NOTHING;
