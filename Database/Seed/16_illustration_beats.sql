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
