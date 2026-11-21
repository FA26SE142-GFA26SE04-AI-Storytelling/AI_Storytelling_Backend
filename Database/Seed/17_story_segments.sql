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

-- Truyện 901: một segment cho mỗi scene (Id segment = Id scene).
INSERT INTO story_segments (
    "Id", "StorySceneId", "SegmentOrder", "StartOffset", "EndOffset",
    "TextContent", "CreatedAt", "IsDeleted")
VALUES
    (901, 901, 1, 0, 50, E'Cuối tuần, bố đưa Phúc đến bảo tàng thiên nhiên.\n\n', NOW() - INTERVAL '2 days', false),
    (902, 902, 1, 0, 92, E'Phúc đứng ngắm bộ xương khủng long rất lớn. Bố kể khủng long sống cách đây hàng triệu năm.\n\n', NOW() - INTERVAL '2 days', false),
    (903, 903, 1, 0, 128, E'Ở phòng bên, Phúc thấy một hóa thạch hình vỏ ốc. Cô hướng dẫn viên nói hóa thạch là dấu vết của sinh vật xưa còn lưu trong đá.\n\n', NOW() - INTERVAL '2 days', false),
    (904, 904, 1, 0, 63, E'Trên đường về, Phúc hứa sẽ đọc thêm sách về khủng long cùng bố.', NOW() - INTERVAL '2 days', false)
ON CONFLICT ("Id") DO NOTHING;
