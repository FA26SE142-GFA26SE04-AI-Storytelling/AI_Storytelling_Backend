-- 31. learning_insights (10)
INSERT INTO learning_insights ("Id", "ChildProfileId", "Observation", "Evidence", "Status", "DetectedAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 'Trẻ đọc nhanh hơn mức trung bình của độ tuổi', 'Thời gian hoàn thành giảm 20% so với tuần trước', 'InsightDetected', NOW() - INTERVAL '9 days', NOW() - INTERVAL '9 days', false),
    (2, 2, 'Trẻ gặp khó khăn với từ vựng mới', 'Tỷ lệ trả lời đúng từ vựng giảm còn 60%', 'Reviewed', NOW() - INTERVAL '8 days', NOW() - INTERVAL '8 days', false),
    (3, 3, 'Trẻ rất thích thể loại cổ tích', 'Đã đọc 5 truyện cổ tích trong tuần', 'InsightDetected', NOW() - INTERVAL '7 days', NOW() - INTERVAL '7 days', false),
    (4, 4, 'Trẻ có khả năng suy luận tốt', 'Trả lời đúng 90% câu hỏi suy luận', 'Reviewed', NOW() - INTERVAL '6 days', NOW() - INTERVAL '6 days', false),
    (5, 5, 'Trẻ ít hoàn thành bài đọc được giao', 'Chỉ hoàn thành 2/5 bài được giao', 'InsightDetected', NOW() - INTERVAL '5 days', NOW() - INTERVAL '5 days', false),
    (6, 6, 'Trẻ tiến bộ rõ rệt về tốc độ đọc', 'Tốc độ đọc tăng 30% trong tháng', 'Reviewed', NOW() - INTERVAL '4 days', NOW() - INTERVAL '4 days', false),
    (7, 7, 'Trẻ cần hỗ trợ thêm về phát âm', 'Ghi nhận nhiều lỗi phát âm trong AsrWordResult', 'InsightDetected', NOW() - INTERVAL '3 days', NOW() - INTERVAL '3 days', false),
    (8, 8, 'Trẻ rất tích cực tham gia thảo luận', 'Trả lời 100% câu hỏi thảo luận', 'Reviewed', NOW() - INTERVAL '2 days', NOW() - INTERVAL '2 days', false),
    (9, 9, 'Trẻ ít tương tác với ứng dụng gần đây', 'Số phiên đọc giảm 50% so với tháng trước', 'InsightDetected', NOW() - INTERVAL '1 days', NOW() - INTERVAL '1 days', false),
    (10, 10, 'Trẻ thích thể loại thể thao và phiêu lưu', 'Đã đọc và đánh dấu yêu thích 4 truyện thể thao', 'Reviewed', NOW() - INTERVAL '1 days', NOW() - INTERVAL '1 days', false)
ON CONFLICT ("Id") DO NOTHING;
