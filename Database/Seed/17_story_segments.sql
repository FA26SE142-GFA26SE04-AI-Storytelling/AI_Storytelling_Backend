-- 17. story_segments (11)
-- Seed story_segments cho moi StoryScene de thoa man khoa ngoai va check constraint audio
INSERT INTO story_segments (
    "Id", "StorySceneId", "SegmentOrder", "StartOffset", "EndOffset",
    "TextContent", "CreatedAt", "IsDeleted")
SELECT
    s."Id",
    s."Id",
    1,
    0,
    char_length(s."SceneText"),
    s."SceneText",
    s."CreatedAt",
    false
FROM story_scenes AS s
WHERE s."Id" IN (1, 3, 6, 10, 12, 14, 15, 16, 17, 18, 19)
ON CONFLICT ("Id") DO UPDATE SET
    "StorySceneId" = EXCLUDED."StorySceneId",
    "SegmentOrder" = EXCLUDED."SegmentOrder",
    "StartOffset" = EXCLUDED."StartOffset",
    "EndOffset" = EXCLUDED."EndOffset",
    "TextContent" = EXCLUDED."TextContent",
    "IsDeleted" = false;
