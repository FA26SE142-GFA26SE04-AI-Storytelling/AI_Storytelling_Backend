-- 21. media_assets (10)
INSERT INTO media_assets ("Id", "StoryVersionId", "Type", "Url", "Status", "SceneIndex", "WordTimings", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 'Illustration', 'https://example.com/media/story1-scene1.png', 'Ready', 1, NULL, NOW() - INTERVAL '30 days', false),
    (2, 2, 'TtsAudio', 'https://example.com/media/story2-audio1.mp3', 'Ready', 1, '[{"word":"Trong","start":0.0,"end":0.4},{"word":"khu","start":0.5,"end":0.8},{"word":"rung","start":0.9,"end":1.2}]', NOW() - INTERVAL '28 days', false),
    (3, 3, 'Illustration', 'https://example.com/media/story3-scene1.png', 'Ready', 1, NULL, NOW() - INTERVAL '26 days', false),
    (4, 4, 'Illustration', 'https://example.com/media/story4-scene1.png', 'Processing', 1, NULL, NOW() - INTERVAL '24 days', false),
    (5, 5, 'TtsAudio', 'https://example.com/media/story5-audio1.mp3', 'Ready', 1, '[{"word":"Hai","start":0.0,"end":0.3},{"word":"nguoi","start":0.4,"end":0.7},{"word":"ban","start":0.8,"end":1.1}]', NOW() - INTERVAL '22 days', false),
    (6, 6, 'Illustration', 'https://example.com/media/story6-scene1.png', 'Ready', 1, NULL, NOW() - INTERVAL '20 days', false),
    (7, 7, 'TtsAudio', 'https://example.com/media/story7-audio1.mp3', 'Queued', 1, '[{"word":"Moi","start":0.0,"end":0.3},{"word":"buoi","start":0.4,"end":0.7},{"word":"toi","start":0.8,"end":1.0}]', NOW() - INTERVAL '19 days', false),
    (8, 8, 'Illustration', 'https://example.com/media/story8-scene1.png', 'Ready', 1, NULL, NOW() - INTERVAL '17 days', false),
    (9, 9, 'Illustration', 'https://example.com/media/story9-scene1.png', 'Failed', 1, NULL, NOW() - INTERVAL '15 days', false),
    (10, 10, 'TtsAudio', 'https://example.com/media/story10-audio1.mp3', 'Ready', 1, '[{"word":"Tran","start":0.0,"end":0.3},{"word":"chung","start":0.4,"end":0.7},{"word":"ket","start":0.8,"end":1.1}]', NOW() - INTERVAL '13 days', false)
ON CONFLICT ("Id") DO NOTHING;
