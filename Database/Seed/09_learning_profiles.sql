-- 9. learning_profiles (10 - unique theo ChildProfileId)
INSERT INTO learning_profiles (
    "Id", "ChildProfileId", "ReadingLevel", "ComprehensionGoal", "CreatedAt", "IsDeleted", "ReadingMode"
)
VALUES
    (1, 1, 2, 'Hiểu được ý chính của câu chuyện', NOW() - INTERVAL '40 days', false, 'ListenFirst'),
    (2, 2, 3, 'Tóm tắt được diễn biến câu chuyện', NOW() - INTERVAL '39 days', false, 'ReadFirst'),
    (3, 3, 1, 'Nhận biết nhân vật chính', NOW() - INTERVAL '38 days', false, 'ListenFirst'),
    (4, 4, 4, 'Suy luận được bài học rút ra', NOW() - INTERVAL '37 days', false, 'ReadFirst'),
    (5, 5, 2, 'Đọc trôi chảy đoạn văn ngắn', NOW() - INTERVAL '36 days', false, 'ListenFirst'),
    (6, 6, 3, 'Hiểu mối quan hệ nhân quả trong truyện', NOW() - INTERVAL '35 days', false, 'ReadFirst'),
    (7, 7, 1, 'Nhận biết từ vựng cơ bản', NOW() - INTERVAL '34 days', false, 'ListenFirst'),
    (8, 8, 5, 'Phân tích được động cơ nhân vật', NOW() - INTERVAL '33 days', false, 'ReadFirst'),
    (9, 9, 2, 'Đọc hiểu câu đơn giản', NOW() - INTERVAL '32 days', false, 'ListenFirst'),
    (10, 10, 3, 'Kể lại được câu chuyện bằng lời của mình', NOW() - INTERVAL '18 days', false, 'ReadFirst')
ON CONFLICT ("Id") DO NOTHING;
