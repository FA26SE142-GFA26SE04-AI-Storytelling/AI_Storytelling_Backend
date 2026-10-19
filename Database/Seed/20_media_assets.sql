-- 20. media_assets (24: 22 active + 2 soft-deleted historical records)
-- Url luu object path trong private Supabase bucket, khong luu public/signed URL.
-- Selects from a VALUES list (rather than a plain INSERT) so rows whose StorySceneId
-- or StorySegmentId doesn't exist yet (15_story_scenes.sql / 17_story_segments.sql are
-- themselves conditional on story_versions.Content) are skipped instead of violating the FK.
-- For this fixture each illustration beat has the same Id as its ready scene.
INSERT INTO media_assets ("Id", "StoryVersionId", "StorySceneId", "IllustrationBeatId", "StorySegmentId", "Type", "Url", "Status", "ValidationStatus", "SceneIndex", "WordTimings", "AttemptCount", "CreatedAt", "IsDeleted")
SELECT v."Id", v."StoryVersionId", v."StorySceneId",
    CASE WHEN v."Type" = 'Illustration' THEN v."StorySceneId" ELSE NULL END,
    v."StorySegmentId", v."Type", v."Url", v."Status", v."ValidationStatus", v."SceneIndex", v."WordTimings", 0, v."CreatedAt", v."IsDeleted"
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
    (10, 10, NULL, 10, 'TtsAudio', NULL, 'Failed', 'Failed', NULL, NULL, NOW() - INTERVAL '13 days', true),
    (11, 12, 12, NULL, 'Illustration', '12/scene-0.png', 'Ready', 'Passed', 0, NULL, NOW() - INTERVAL '1 day', false),
    (12, 12, 12, 12, 'TtsAudio', '12/audio-0.mp3', 'Ready', 'Passed', 0, '[{"word":"Lan","start":0.0,"end":0.4}]', NOW() - INTERVAL '1 day', false),
    (13, 14, 14, NULL, 'Illustration', '14/scene-0.png', 'Ready', 'Passed', 0, NULL, NOW() - INTERVAL '7 days', false),
    (14, 14, 14, 14, 'TtsAudio', '14/audio-0.mp3', 'Ready', 'Passed', 0, '[{"word":"Mai","start":0.0,"end":0.4}]', NOW() - INTERVAL '7 days', false),
    (15, 15, 15, NULL, 'Illustration', '15/scene-0.png', 'Ready', 'Passed', 0, NULL, NOW() - INTERVAL '2 days', false),
    (16, 15, 15, 15, 'TtsAudio', '15/audio-0.mp3', 'Ready', 'Passed', 0, '[{"word":"Mai","start":0.0,"end":0.4}]', NOW() - INTERVAL '2 days', false),
    (17, 16, 16, NULL, 'Illustration', '16/scene-0.png', 'Ready', 'Passed', 0, NULL, NOW() - INTERVAL '2 hours', false),
    (18, 16, 16, 16, 'TtsAudio', '16/audio-0.mp3', 'Ready', 'Passed', 0, '[{"word":"Mai","start":0.0,"end":0.4}]', NOW() - INTERVAL '2 hours', false),
    (19, 17, 17, NULL, 'Illustration', '17/scene-0.png', 'Ready', 'Passed', 0, NULL, NOW() - INTERVAL '8 days', false),
    (20, 17, 17, 17, 'TtsAudio', '17/audio-0.mp3', 'Ready', 'Passed', 0, '[{"word":"An","start":0.0,"end":0.3}]', NOW() - INTERVAL '8 days', false),
    (21, 18, 18, NULL, 'Illustration', '18/scene-0.png', 'Ready', 'Passed', 0, NULL, NOW() - INTERVAL '1 day', false),
    (22, 18, 18, 18, 'TtsAudio', '18/audio-0.mp3', 'Ready', 'Passed', 0, '[{"word":"An","start":0.0,"end":0.3}]', NOW() - INTERVAL '1 day', false),
    (23, 19, 19, NULL, 'Illustration', '19/scene-0.png', 'Ready', 'Passed', 0, NULL, NOW() - INTERVAL '1 hour', false),
    (24, 19, 19, 19, 'TtsAudio', '19/audio-0.mp3', 'Ready', 'Passed', 0, '[{"word":"An","start":0.0,"end":0.3}]', NOW() - INTERVAL '1 hour', false)
) AS v("Id", "StoryVersionId", "StorySceneId", "StorySegmentId", "Type", "Url", "Status", "ValidationStatus", "SceneIndex", "WordTimings", "CreatedAt", "IsDeleted")
WHERE (v."StorySceneId" IS NULL OR EXISTS (SELECT 1 FROM story_scenes s WHERE s."Id" = v."StorySceneId"))
  AND (v."StorySegmentId" IS NULL OR EXISTS (SELECT 1 FROM story_segments sg WHERE sg."Id" = v."StorySegmentId"))
  AND (v."Type" <> 'Illustration' OR v."StorySceneId" IS NULL OR EXISTS (SELECT 1 FROM illustration_beats b WHERE b."Id" = v."StorySceneId" AND b."StorySceneId" = v."StorySceneId"))
ON CONFLICT ("Id") DO UPDATE SET
    "StoryVersionId" = EXCLUDED."StoryVersionId",
    "StorySceneId" = EXCLUDED."StorySceneId",
    "IllustrationBeatId" = EXCLUDED."IllustrationBeatId",
    "StorySegmentId" = EXCLUDED."StorySegmentId",
    "Type" = EXCLUDED."Type",
    "Url" = EXCLUDED."Url",
    "Status" = EXCLUDED."Status",
    "ValidationStatus" = EXCLUDED."ValidationStatus",
    "SceneIndex" = EXCLUDED."SceneIndex",
    "WordTimings" = EXCLUDED."WordTimings",
    "AttemptCount" = EXCLUDED."AttemptCount",
    "IsDeleted" = EXCLUDED."IsDeleted";
