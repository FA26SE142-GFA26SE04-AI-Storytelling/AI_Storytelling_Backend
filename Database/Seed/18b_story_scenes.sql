-- 18b. story_scenes (4)
-- Seed moi StoryVersion Ready thanh mot scene bao phu chinh xac toan bo Content.
INSERT INTO story_scenes (
    "Id", "StoryVersionId", "SceneIndex", "TextRangeStart", "TextRangeEnd",
    "SceneText", "VisualDescription", "CreatedAt", "IsDeleted")
SELECT
    v."Id",
    v."Id",
    0,
    0,
    char_length(v."Content"),
    v."Content",
    CASE v."Id"
        WHEN 1 THEN 'A clever rabbit safely solving a problem in a warm, colorful forest.'
        WHEN 3 THEN 'A kind child in a bright fairy-tale kingdom where compassion wins.'
        WHEN 6 THEN 'A child-friendly underwater discovery with vivid sea life and clean oceans.'
        WHEN 10 THEN 'A joyful school football match highlighting teamwork and fair play.'
    END,
    v."CreatedAt",
    false
FROM story_versions AS v
WHERE v."Id" IN (1, 3, 6, 10)
  AND v."Content" IS NOT NULL
  AND v."Content" <> ''
ON CONFLICT ("Id") DO UPDATE SET
    "StoryVersionId" = EXCLUDED."StoryVersionId",
    "SceneIndex" = EXCLUDED."SceneIndex",
    "TextRangeStart" = EXCLUDED."TextRangeStart",
    "TextRangeEnd" = EXCLUDED."TextRangeEnd",
    "SceneText" = EXCLUDED."SceneText",
    "VisualDescription" = EXCLUDED."VisualDescription",
    "IsDeleted" = false;
