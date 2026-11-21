-- 15. story_scenes (11)
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
        WHEN 12 THEN 'Lan and Minh happily caring for a small seedling in a sunny garden.'
        WHEN 14 THEN 'Mai patiently watering a colorful school flower garden.'
        WHEN 15 THEN 'Mai and Nam standing near flowers in the school garden.'
        WHEN 16 THEN 'Mai and Nam asking permission, caring for flowers, and sharing them with their class.'
        WHEN 17 THEN 'An holding a small guiding lamp in a calm evening scene.'
        WHEN 18 THEN 'An and friends noticing an unsafe lamp near a curtain, without frightening detail.'
        WHEN 19 THEN 'An placing a safe gentle lamp by the window while friends find their way home.'
    END,
    v."CreatedAt",
    false
FROM story_versions AS v
WHERE v."Id" IN (1, 3, 6, 10, 12, 14, 15, 16, 17, 18, 19)
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

-- Truyện 901 (tương tác điền từ): mỗi đoạn một scene, TextRangeEnd = cuối đoạn (gồm dòng trống) như
-- StoryBlockParser tạo, để tương tác neo cuối đoạn 2 gắn được vào scene 902 (ListeningInteractionSceneBinder).
INSERT INTO story_scenes (
    "Id", "StoryVersionId", "SceneIndex", "TextRangeStart", "TextRangeEnd",
    "SceneText", "VisualDescription", "CreatedAt", "IsDeleted")
VALUES
    (901, 901, 0, 0, 50, E'Cuối tuần, bố đưa Phúc đến bảo tàng thiên nhiên.\n\n', 'A child and his father arriving at a bright natural history museum.', NOW() - INTERVAL '2 days', false),
    (902, 901, 1, 50, 142, E'Phúc đứng ngắm bộ xương khủng long rất lớn. Bố kể khủng long sống cách đây hàng triệu năm.\n\n', 'A child looking up at a large dinosaur skeleton with his father.', NOW() - INTERVAL '2 days', false),
    (903, 901, 2, 142, 270, E'Ở phòng bên, Phúc thấy một hóa thạch hình vỏ ốc. Cô hướng dẫn viên nói hóa thạch là dấu vết của sinh vật xưa còn lưu trong đá.\n\n', 'A museum guide showing a spiral shell fossil to the child.', NOW() - INTERVAL '2 days', false),
    (904, 901, 3, 270, 333, E'Trên đường về, Phúc hứa sẽ đọc thêm sách về khủng long cùng bố.', 'A child and his father walking home happily with a dinosaur book.', NOW() - INTERVAL '2 days', false)
ON CONFLICT ("Id") DO NOTHING;
