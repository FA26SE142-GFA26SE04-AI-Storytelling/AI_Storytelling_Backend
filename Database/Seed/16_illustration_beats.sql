-- 16. illustration_beats (11): one ordered visual beat for each seeded ready scene.
INSERT INTO illustration_beats (
    "Id", "StorySceneId", "BeatOrder", "StartOffset", "EndOffset",
    "VisualFocus", "CreatedAt", "IsDeleted"
)
SELECT
    s."Id", s."Id", 1, 0, char_length(s."SceneText"),
    left(s."VisualDescription", 500), s."CreatedAt", false
FROM story_scenes AS s
WHERE s."Id" IN (1, 3, 6, 10, 12, 14, 15, 16, 17, 18, 19)
  AND s."SceneText" IS NOT NULL
  AND s."SceneText" <> ''
  AND s."VisualDescription" IS NOT NULL
ON CONFLICT ("Id") DO UPDATE SET
    "StorySceneId" = EXCLUDED."StorySceneId",
    "BeatOrder" = EXCLUDED."BeatOrder",
    "StartOffset" = EXCLUDED."StartOffset",
    "EndOffset" = EXCLUDED."EndOffset",
    "VisualFocus" = EXCLUDED."VisualFocus",
    "IsDeleted" = false;

-- Truyện 901: một beat cho mỗi scene (Id beat = Id scene).
INSERT INTO illustration_beats (
    "Id", "StorySceneId", "BeatOrder", "StartOffset", "EndOffset",
    "VisualFocus", "CreatedAt", "IsDeleted"
)
VALUES
    (901, 901, 1, 0, 50, 'A child and his father arriving at a bright natural history museum.', NOW() - INTERVAL '2 days', false),
    (902, 902, 1, 0, 92, 'A child looking up at a large dinosaur skeleton with his father.', NOW() - INTERVAL '2 days', false),
    (903, 903, 1, 0, 128, 'A museum guide showing a spiral shell fossil to the child.', NOW() - INTERVAL '2 days', false),
    (904, 904, 1, 0, 63, 'A child and his father walking home happily with a dinosaur book.', NOW() - INTERVAL '2 days', false)
ON CONFLICT ("Id") DO NOTHING;
