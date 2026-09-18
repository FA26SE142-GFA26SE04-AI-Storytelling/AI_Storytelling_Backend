-- 21. media_assets (10: 8 active + 2 soft-deleted historical records)
-- Url luu object path trong private Supabase bucket, khong luu public/signed URL.
INSERT INTO media_assets ("Id", "StoryVersionId", "StorySceneId", "Type", "Url", "Status", "SceneIndex", "WordTimings", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 1, 'Illustration', '1/scene-0.png', 'Ready', 0, NULL, NOW() - INTERVAL '30 days', false),
    (2, 1, 1, 'TtsAudio', '1/audio-0.mp3', 'Ready', 0, '[{"word":"Ngay","start":0.0,"end":0.3},{"word":"xua","start":0.4,"end":0.7}]', NOW() - INTERVAL '30 days', false),
    (3, 3, 3, 'Illustration', '3/scene-0.png', 'Ready', 0, NULL, NOW() - INTERVAL '26 days', false),
    (4, 3, 3, 'TtsAudio', '3/audio-0.mp3', 'Ready', 0, NULL, NOW() - INTERVAL '26 days', false),
    (5, 6, 6, 'Illustration', '6/scene-0.png', 'Ready', 0, NULL, NOW() - INTERVAL '20 days', false),
    (6, 6, 6, 'TtsAudio', '6/audio-0.mp3', 'Ready', 0, NULL, NOW() - INTERVAL '20 days', false),
    (7, 10, 10, 'Illustration', '10/scene-0.png', 'Ready', 0, NULL, NOW() - INTERVAL '13 days', false),
    (8, 10, 10, 'TtsAudio', '10/audio-0.mp3', 'Ready', 0, '[{"word":"Tran","start":0.0,"end":0.3},{"word":"chung","start":0.4,"end":0.7},{"word":"ket","start":0.8,"end":1.1}]', NOW() - INTERVAL '13 days', false),
    (9, 9, NULL, 'Illustration', NULL, 'Failed', NULL, NULL, NOW() - INTERVAL '15 days', true),
    (10, 10, NULL, 'TtsAudio', NULL, 'Failed', NULL, NULL, NOW() - INTERVAL '13 days', true)
ON CONFLICT ("Id") DO UPDATE SET
    "StoryVersionId" = EXCLUDED."StoryVersionId",
    "StorySceneId" = EXCLUDED."StorySceneId",
    "Type" = EXCLUDED."Type",
    "Url" = EXCLUDED."Url",
    "Status" = EXCLUDED."Status",
    "SceneIndex" = EXCLUDED."SceneIndex",
    "WordTimings" = EXCLUDED."WordTimings",
    "IsDeleted" = EXCLUDED."IsDeleted";
