-- 83. read_aloud_recordings (3)
-- Bản ghi đọc to mẫu của các bé KHÔNG thuộc parent_demo1 (test guide đọc to dùng phiên của bé 1–2).
-- AudioStoragePath là đường dẫn GIẢ theo mẫu ChildReadAloudService (read-aloud/{bé}/{phiên}/segment-{đoạn}/...):
-- KHÔNG có file thật trong storage, nên danh sách và điểm hiện đúng nhưng mở link nghe sẽ lỗi.
-- Mỗi dòng nhắm đúng một đoạn truyện (CK_read_aloud_recordings_one_target), không trùng (phiên, đoạn).
-- TotalCount = số chữ của đoạn: đoạn 3 (bản 3) có 8 chữ, đoạn 17 (bản 17) có 15 chữ; Good + Retry + Skipped = Total.
-- Dòng mẫu:
--   Bé 3 Chi, phiên 3, đoạn 3.
--   Bé 3 Chi, phiên 17, đoạn 17.
--   Bé 7 Giang, phiên 16, đoạn 17.
INSERT INTO read_aloud_recordings (
    "Id", "ReadingSessionId", "ChildProfileId", "StorySegmentId", "ListeningInteractionId",
    "AudioStoragePath", "AudioContentType", "AudioBytes",
    "GoodCount", "RetryCount", "SkippedCount", "TotalCount",
    "RecordedAt", "CreatedAt", "IsDeleted"
)
VALUES
    (1, 3, 3, 3, NULL,
     'read-aloud/3/3/segment-3/seed-recording-1.webm', 'audio/webm', 36000,
     6, 1, 1, 8,
     NOW() - INTERVAL '8 days' + INTERVAL '3 minutes', NOW() - INTERVAL '8 days' + INTERVAL '3 minutes', false),
    (2, 17, 3, 17, NULL,
     'read-aloud/3/17/segment-17/seed-recording-2.webm', 'audio/webm', 62000,
     13, 2, 0, 15,
     NOW() - INTERVAL '1 day' + INTERVAL '2 minutes', NOW() - INTERVAL '1 day' + INTERVAL '2 minutes', false),
    (3, 16, 7, 17, NULL,
     'read-aloud/7/16/segment-17/seed-recording-3.webm', 'audio/webm', 58000,
     9, 4, 2, 15,
     NOW() - INTERVAL '2 days' + INTERVAL '3 minutes', NOW() - INTERVAL '2 days' + INTERVAL '3 minutes', false)
ON CONFLICT ("Id") DO NOTHING;
