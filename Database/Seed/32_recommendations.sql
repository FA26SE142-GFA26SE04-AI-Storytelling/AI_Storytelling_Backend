-- 32. recommendations (10)
INSERT INTO recommendations ("Id", "ChildProfileId", "LearningInsightId", "Category", "CurrentState", "ProposedChange", "Evidence", "Status", "ExpiresAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 1, 'ReadingDifficulty', 'Đang ở mức độ 2', 'Đề xuất nâng lên mức độ 3', 'Điểm quiz đạt 90% trong 3 lần gần nhất', 'AwaitingReview', NOW() - INTERVAL '8 days' + INTERVAL '14 days', NOW() - INTERVAL '8 days', false),
    (2, 2, 2, 'LearningFocus', 'Tập trung vào đọc hiểu', 'Đề xuất tập trung ôn từ vựng', 'Tỷ lệ trả lời đúng từ vựng giảm còn 60%', 'Detected', NULL, NOW() - INTERVAL '7 days', false),
    (3, 3, 3, 'ContentPersonalization', 'Nội dung đa thể loại', 'Đề xuất ưu tiên nội dung cổ tích', 'Đã đọc 5 truyện cổ tích trong tuần', 'Approved', NULL, NOW() - INTERVAL '6 days', false),
    (4, 4, 4, 'ReadingDifficulty', 'Đang ở mức độ 4', 'Đề xuất giữ nguyên mức độ', 'Trả lời đúng 90% câu hỏi suy luận', 'Applied', NULL, NOW() - INTERVAL '5 days', false),
    (5, 5, 5, 'LearningIntervention', 'Hoàn thành bài giao thấp', 'Đề xuất can thiệp, nhắc nhở phụ huynh', 'Chỉ hoàn thành 2/5 bài được giao', 'RecommendationCreated', NULL, NOW() - INTERVAL '4 days', false),
    (6, 6, 6, 'ReadingDifficulty', 'Đang ở mức độ 3', 'Đề xuất nâng lên mức độ 4', 'Tốc độ đọc tăng 30% trong tháng', 'Validated', NULL, NOW() - INTERVAL '3 days', false),
    (7, 7, 7, 'LearningIntervention', 'Nhiều lỗi phát âm', 'Đề xuất bổ sung bài luyện phát âm', 'Ghi nhận nhiều lỗi phát âm trong AsrWordResult', 'Modified', NULL, NOW() - INTERVAL '2 days', false),
    (8, 8, 8, 'ContentPersonalization', 'Tích cực thảo luận', 'Đề xuất thêm câu hỏi thảo luận nâng cao', 'Trả lời 100% câu hỏi thảo luận', 'Monitoring', NULL, NOW() - INTERVAL '1 days', false),
    (9, 9, 9, 'LearningIntervention', 'Giảm tương tác', 'Đề xuất gửi nhắc nhở hằng ngày', 'Số phiên đọc giảm 50% so với tháng trước', 'ObserveMore', NULL, NOW() - INTERVAL '1 days', false),
    (10, 10, 10, 'ContentPersonalization', 'Thích thể thao, phiêu lưu', 'Đề xuất ưu tiên truyện thể thao', 'Đã đọc và đánh dấu yêu thích 4 truyện thể thao', 'ReassessmentRequired', NULL, NOW() - INTERVAL '1 days', false)
ON CONFLICT ("Id") DO NOTHING;
