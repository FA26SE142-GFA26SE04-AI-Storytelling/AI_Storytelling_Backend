-- 21. media_assets (10: 8 active + 2 soft-deleted historical records)
-- Url luu object path trong private Supabase bucket, khong luu public/signed URL.
-- Selects from a VALUES list (rather than a plain INSERT) so rows whose StorySceneId
-- or StorySegmentId doesn't exist yet (18b_story_scenes.sql / 18c_story_segments.sql are
-- themselves conditional on story_versions.Content) are skipped instead of violating the FK.
INSERT INTO media_assets ("Id", "StoryVersionId", "StorySceneId", "StorySegmentId", "Type", "Url", "Status", "ValidationStatus", "SceneIndex", "WordTimings", "AttemptCount", "CreatedAt", "IsDeleted")
SELECT v."Id", v."StoryVersionId", v."StorySceneId", v."StorySegmentId", v."Type", v."Url", v."Status", v."ValidationStatus", v."SceneIndex", v."WordTimings", 0, v."CreatedAt", v."IsDeleted"
FROM (VALUES
    (1, 1, 1, NULL, 'Illustration', '1/scene-0.png', 'Ready', 'Passed', 0, NULL, NOW() - INTERVAL '30 days', false),
    (2, 1, 1, 1, 'TtsAudio', '1/audio-0.mp3', 'Ready', 'Passed', 0, '[{"word":"Ngay","start":0.0,"end":0.3},{"word":"xua","start":0.4,"end":0.7}]', NOW() - INTERVAL '30 days', false),
    (3, 3, 3, NULL, 'Illustration', '3/scene-0.png', 'Ready', 'Passed', 0, NULL, NOW() - INTERVAL '26 days', false),
    (4, 3, 3, 3, 'TtsAudio', '3/audio-0.mp3', 'Ready', 'Passed', 0, NULL, NOW() - INTERVAL '26 days', false),
    (5, 6, 6, NULL, 'Illustration', '6/scene-0.png', 'Ready', 'Passed', 0, NULL, NOW() - INTERVAL '20 days', false),
    (6, 6, 6, 6, 'TtsAudio', '6/audio-0.mp3', 'Ready', 'Passed', 0, NULL, NOW() - INTERVAL '20 days', false),
    (7, 10, 10, NULL, 'Illustration', '10/scene-0.png', 'Ready', 'Passed', 0, NULL, NOW() - INTERVAL '13 days', false),
    (8, 10, 10, 10, 'TtsAudio', '10/audio-0.mp3', 'Ready', 'Passed', 0, '[{"word":"Tran","start":0.0,"end":0.3},{"word":"chung","start":0.4,"end":0.7},{"word":"ket","start":0.8,"end":1.1}]', NOW() - INTERVAL '13 days', false),
    (9, 9, NULL, NULL, 'Illustration', NULL, 'Failed', 'Failed', NULL, NULL, NOW() - INTERVAL '15 days', true),
    (10, 10, NULL, 10, 'TtsAudio', NULL, 'Failed', 'Failed', NULL, NULL, NOW() - INTERVAL '13 days', true)
) AS v("Id", "StoryVersionId", "StorySceneId", "StorySegmentId", "Type", "Url", "Status", "ValidationStatus", "SceneIndex", "WordTimings", "CreatedAt", "IsDeleted")
WHERE (v."StorySceneId" IS NULL OR EXISTS (SELECT 1 FROM story_scenes s WHERE s."Id" = v."StorySceneId"))
  AND (v."StorySegmentId" IS NULL OR EXISTS (SELECT 1 FROM story_segments sg WHERE sg."Id" = v."StorySegmentId"))
ON CONFLICT ("Id") DO UPDATE SET
    "StoryVersionId" = EXCLUDED."StoryVersionId",
    "StorySceneId" = EXCLUDED."StorySceneId",
    "StorySegmentId" = EXCLUDED."StorySegmentId",
    "Type" = EXCLUDED."Type",
    "Url" = EXCLUDED."Url",
    "Status" = EXCLUDED."Status",
    "ValidationStatus" = EXCLUDED."ValidationStatus",
    "SceneIndex" = EXCLUDED."SceneIndex",
    "WordTimings" = EXCLUDED."WordTimings",
    "AttemptCount" = EXCLUDED."AttemptCount",
    "IsDeleted" = EXCLUDED."IsDeleted";
