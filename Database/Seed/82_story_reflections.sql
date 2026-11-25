-- 82. story_reflections (5)
-- Cảm nhận sau khi đọc của các bé KHÔNG thuộc parent_demo1, để các test guide đang dùng phiên của bé 1–2
-- (phiên 1, 2, 11, 13, 101–104) vẫn bắt đầu từ trạng thái "chưa có cảm nhận".
-- Mỗi phiên tối đa 1 dòng (UX_story_reflections_one_per_session). Emotions là tên StoryEmotion, 0–2 phần tử.
-- FavoriteSceneId / FavoriteMediaAssetId lấy từ cảnh và tranh đã seed của đúng phiên bản truyện:
-- bản 3 -> cảnh 3, tranh 3; bản 17 -> cảnh 17, tranh 19. Bản 8 và 21 không có cảnh nên chỉ có cảm xúc.
-- Dòng 2 có ghi âm GIẢ: đường dẫn không có file thật trong storage, nên link nghe trả lỗi khi mở.
-- Dòng mẫu:
--   Bé 3 Chi, phiên 3 (truyện 3): 2 cảm xúc, cảnh thích nhất và lời viết.
--   Bé 3 Chi, phiên 17 (truyện 14): 1 cảm xúc, cảnh thích nhất và ghi âm giả.
--   Bé 7 Giang, phiên 16 (truyện 14): 2 cảm xúc + cảnh thích nhất, không gửi lời.
--   Bé 6 Phúc, phiên 14 (truyện 16): chỉ chọn cảm xúc (bản 21 không có cảnh).
--   Bé 8 Hạnh, phiên 8 (truyện 8): cảm xúc + lời viết, không có cảnh.
INSERT INTO story_reflections (
    "Id", "ReadingSessionId", "ChildProfileId", "StoryId", "StoryVersionId", "Emotions",
    "FavoriteSceneId", "FavoriteMediaAssetId", "NoteText",
    "NoteAudioStoragePath", "NoteAudioContentType", "NoteAudioBytes", "NoteSentAt",
    "CreatedAt", "UpdatedAt", "IsDeleted"
)
VALUES
    (1, 3, 3, 3, 3, '{Happy,Touched}',
     3, 3, 'Con thích nhất lúc bạn nhỏ giúp mọi người trong vương quốc.',
     NULL, NULL, NULL, NOW() - INTERVAL '8 days' + INTERVAL '10 minutes',
     NOW() - INTERVAL '8 days' + INTERVAL '9 minutes', NOW() - INTERVAL '8 days' + INTERVAL '10 minutes', false),
    (2, 17, 3, 14, 17, '{Excited}',
     17, 19, NULL,
     'story-reflections/3/17/seed-reflection-2.webm', 'audio/webm', 48000, NOW() - INTERVAL '1 day' + INTERVAL '7 minutes',
     NOW() - INTERVAL '1 day' + INTERVAL '6 minutes', NOW() - INTERVAL '1 day' + INTERVAL '7 minutes', false),
    (3, 16, 7, 14, 17, '{Surprised,Happy}',
     17, 19, NULL,
     NULL, NULL, NULL, NULL,
     NOW() - INTERVAL '2 days' + INTERVAL '7 minutes', NULL, false),
    (4, 14, 6, 16, 21, '{Funny}',
     NULL, NULL, NULL,
     NULL, NULL, NULL, NULL,
     NOW() - INTERVAL '3 days' + INTERVAL '5 minutes', NULL, false),
    (5, 8, 8, 8, 8, '{Excited,Funny}',
     NULL, NULL, 'Siêu nhân tí hon rất dũng cảm!',
     NULL, NULL, NULL, NOW() - INTERVAL '3 days' + INTERVAL '14 minutes',
     NOW() - INTERVAL '3 days' + INTERVAL '13 minutes', NOW() - INTERVAL '3 days' + INTERVAL '14 minutes', false)
ON CONFLICT ("Id") DO NOTHING;
