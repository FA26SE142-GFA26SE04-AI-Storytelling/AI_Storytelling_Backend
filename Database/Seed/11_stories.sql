-- 11. stories (19: 12 Parent, 7 Creator)
-- Creator: Id 11 = manual V2, Id 12 = imported text V1; author is creator_demo (Id 13).
-- WF-L5A fixtures: Id 11 = first submit/withdraw; Id 13 = creator resubmit;
-- Id 14 = recent pending resubmission for moderator queue/history/compare;
-- Id 16 = public, current version approved, no open item: removal request -> decision -> revisions;
-- Id 17 = approved version already removed by a moderator: revisions succeeds directly;
-- Id 18 = first submission still Pending (item 26), reserved for the creator withdraw test.
-- ON CONFLICT preserves existing rows; this file does not overwrite live authoring data.
INSERT INTO stories (
    "Id", "Title", "Description", "Content", "CoverImageUrl", "Genre", "MoralLesson", "Language", "AgeBand", "ReadingLevel", "VocabularyLevel", "Status", "Source", "IsPublished", "ChildVisibleAt", "ArchivedReason", "AuthorUserId", "ChildProfileId", "CreatedAt", "IsDeleted", "Scope", "OutputMode", "AudienceRevision", "AuthoringLastVersionNo", "AuthoringRevisionToken", "ExperienceMode"
)
VALUES
    (1, 'Chú Thỏ Thông Minh', 'Câu chuyện về chú thỏ biết dùng trí thông minh để thoát hiểm', 'Ngày xưa, có một chú thỏ rất thông minh sống trong khu rừng...', 'https://example.com/covers/story1.png', 'Ngụ ngôn', 'Sự thông minh giúp vượt qua khó khăn', 'vi', '6-8', 2, 'level_2', 'Ready', 'Manual', true, NOW() - INTERVAL '30 days' + INTERVAL '2 hours', NULL, 2, 1, NOW() - INTERVAL '30 days', false, 'Parent', 'Learning', 1, 1, md5('seed-parent-story-1'), 'reading'),
    (2, 'Rừng Xanh Kỳ Diệu', 'Hành trình khám phá khu rừng đầy màu sắc', 'Trong khu rừng xanh có rất nhiều điều kỳ diệu đang chờ được khám phá...', 'https://example.com/covers/story2.png', 'Phiêu lưu', 'Trân trọng thiên nhiên', 'vi', '9-10', 4, 'level_4', 'Draft', 'Ai', false, NULL, NULL, 7, 2, NOW() - INTERVAL '28 days', false, 'Parent', 'Learning', 1, 1, md5('seed-parent-story-2'), 'reading'),
    (3, 'Vương Quốc Cổ Tích', 'Một vương quốc nơi phép thuật và lòng tốt ngự trị', 'Ngày xưa ở một vương quốc xa xôi...', 'https://example.com/covers/story3.png', 'Cổ tích', 'Lòng tốt sẽ được đền đáp', 'vi', '6-8', 2, 'level_2', 'Ready', 'Manual', true, NOW() - INTERVAL '26 days' + INTERVAL '2 hours', NULL, 3, 3, NOW() - INTERVAL '26 days', false, 'Parent', 'Learning', 1, 1, md5('seed-parent-story-3'), 'reading'),
    (4, 'Hành Trình Vũ Trụ', 'Chuyến phiêu lưu của một nhà du hành nhỏ tuổi', 'Một ngày nọ, bạn nhỏ quyết định du hành lên vũ trụ...', 'https://example.com/covers/story4.png', 'Khoa học viễn tưởng', 'Khám phá và học hỏi không ngừng', 'vi', '9-10', 5, 'level_5', 'ContentReview', 'Ai', false, NULL, NULL, 8, 4, NOW() - INTERVAL '24 days', false, 'Parent', 'Learning', 1, 1, md5('seed-parent-story-4'), 'reading'),
    (5, 'Người Bạn Tốt', 'Câu chuyện về tình bạn chân thành giữa hai đứa trẻ', 'Hai người bạn thân thiết cùng nhau vượt qua khó khăn...', 'https://example.com/covers/story5.png', 'Đời thường', 'Tình bạn chân thành là vô giá', 'vi', '6-8', 3, 'level_3', 'Approved', 'Manual', false, NOW() - INTERVAL '22 days' + INTERVAL '2 hours', NULL, 4, 5, NOW() - INTERVAL '22 days', false, 'Parent', 'Learning', 1, 1, md5('seed-parent-story-5'), 'reading'),
    (6, 'Bí Mật Đại Dương', 'Khám phá thế giới kỳ diệu dưới đáy đại dương', 'Sau lớp sóng biển xanh là cả một thế giới kỳ diệu...', 'https://example.com/covers/story6.png', 'Phiêu lưu', 'Bảo vệ môi trường biển', 'vi', '9-10', 4, 'level_4', 'Ready', 'Ai', true, NOW() - INTERVAL '20 days' + INTERVAL '2 hours', NULL, 9, 6, NOW() - INTERVAL '20 days', false, 'Parent', 'Learning', 1, 1, md5('seed-parent-story-6'), 'reading'),
    (7, 'Gia Đình Hạnh Phúc', 'Những khoảnh khắc ấm áp trong một gia đình nhỏ', 'Mỗi buổi tối, cả gia đình quây quần bên nhau...', 'https://example.com/covers/story7.png', 'Đời thường', 'Yêu thương gia đình', 'vi', '6-8', 2, 'level_2', 'Draft', 'Manual', false, NULL, NULL, 5, 7, NOW() - INTERVAL '19 days', false, 'Parent', 'Learning', 1, 1, md5('seed-parent-story-7'), 'reading'),
    (8, 'Siêu Nhân Tí Hon', 'Cậu bé có sức mạnh đặc biệt bảo vệ xóm làng', 'Trong một ngôi làng nhỏ, có một cậu bé sở hữu sức mạnh đặc biệt...', 'https://example.com/covers/story8.png', 'Siêu anh hùng', 'Dũng cảm bảo vệ người yếu thế', 'vi', '9-10', 5, 'level_5', 'OutlineReview', 'Ai', false, NULL, NULL, 10, 8, NOW() - INTERVAL '17 days', false, 'Parent', 'Learning', 1, 1, md5('seed-parent-story-8'), 'reading'),
    (9, 'Chuyến Phiêu Lưu Rừng Rậm', 'Nhóm bạn cùng nhau khám phá khu rừng bí ẩn', 'Bốn người bạn quyết định khám phá khu rừng phía sau làng...', 'https://example.com/covers/story9.png', 'Phiêu lưu', 'Đoàn kết tạo nên sức mạnh', 'vi', '6-8', 3, 'level_3', 'Rejected', 'Manual', false, NULL, 'SafetyConcern', 6, 9, NOW() - INTERVAL '15 days', false, 'Parent', 'Learning', 1, 1, md5('seed-parent-story-9'), 'reading'),
    (10, 'Trận Bóng Đá Đáng Nhớ', 'Bài học về tinh thần thể thao và fair-play', 'Trận chung kết bóng đá của trường diễn ra đầy kịch tính...', 'https://example.com/covers/story10.png', 'Thể thao', 'Tinh thần fair-play trong thể thao', 'vi', '9-10', 4, 'level_4', 'Ready', 'Ai', true, NOW() - INTERVAL '13 days' + INTERVAL '2 hours', NULL, 7, 10, NOW() - INTERVAL '13 days', false, 'Parent', 'Learning', 1, 1, md5('seed-parent-story-10'), 'reading'),
    (11, 'Hạt giống của Lan', 'Mẫu gửi duyệt và rút yêu cầu WF-L5A', 'Lan gieo một hạt giống nhỏ. Minh giúp Lan tưới nước mỗi sáng. Khi chiếc lá đầu tiên xuất hiện, hai bạn cùng mỉm cười.', NULL, 'Tình bạn', NULL, 'vi', '6-8', NULL, NULL, 'Ready', 'Manual', false, NULL, NULL, 13, NULL, NOW() - INTERVAL '2 days', false, 'Creator', 'ReadingMediaOnly', 1, 2, md5('seed-creator-story-11'), 'reading'),
    (12, 'Chiếc ô tình bạn', 'Mẫu kiểm thử Creator trên Story/StoryVersion dùng chung', 'Thỏ tìm thấy một chiếc ô bên đường. Khi trời mưa, Thỏ mời Sóc cùng trú dưới ô. Hai bạn vui vẻ đi về nhà.', NULL, 'Tình bạn', NULL, 'vi', '6-8', NULL, NULL, 'Draft', 'Manual', false, NULL, NULL, 13, NULL, NOW() - INTERVAL '2 days', false, 'Creator', 'ReadingMediaOnly', 1, 1, md5('seed-creator-story-12'), 'reading'),
    (13, 'Khu vườn biết sẻ chia', 'Mẫu Creator gửi lại sau yêu cầu sửa WF-L5A', 'Mai trồng một luống hoa nhỏ. Nam xin phép hái một bông và cùng Mai chăm sóc khu vườn. Hai bạn chia hoa cho lớp học.', NULL, 'Tình bạn', 'Biết xin phép và sẻ chia', 'vi', '6-8', 2, 'level_2', 'Ready', 'Manual', true, NOW() - INTERVAL '6 days', NULL, 13, NULL, NOW() - INTERVAL '8 days', false, 'Creator', 'ReadingMediaOnly', 1, 3, md5('seed-creator-story-13'), 'reading'),
    (14, 'Ngọn đèn trong đêm', 'Mẫu gửi lại đang chờ để Moderator kiểm tra WF-L5A', 'An đặt ngọn đèn an toàn bên cửa sổ. Ánh sáng dịu giúp các bạn tìm thấy đường về. Cả nhóm cảm ơn An.', NULL, 'Tình bạn', 'Bình tĩnh giúp đỡ mọi người', 'vi', '6-8', 2, 'level_2', 'Ready', 'Manual', true, NOW() - INTERVAL '7 days', NULL, 13, NULL, NOW() - INTERVAL '9 days', false, 'Creator', 'ReadingMediaOnly', 1, 3, md5('seed-creator-story-14'), 'reading'),
    (16, 'Chú mèo biết lắng nghe', 'Mẫu xin gỡ truyện công khai rồi tạo bản sửa', 'Mèo Mun ngồi nghe bạn Gà kể chuyện. Mun không ngắt lời và chờ bạn nói xong. Gà rất vui vì có một người bạn biết lắng nghe.', NULL, 'Tình bạn', 'Biết lắng nghe bạn bè', 'vi', '6-8', 2, 'level_2', 'Ready', 'Manual', true, NOW() - INTERVAL '5 days', NULL, 13, NULL, NOW() - INTERVAL '6 days', false, 'Creator', 'ReadingMediaOnly', 1, 1, md5('seed-creator-story-16'), 'reading'),
    (17, 'Bạn Rùa đúng hẹn', 'Mẫu đã được Moderator gỡ, sẵn sàng tạo bản sửa', 'Rùa hẹn Thỏ đến sân chơi lúc chiều. Trời mưa nhỏ nhưng Rùa vẫn mang ô đến đúng giờ. Thỏ cảm ơn Rùa vì đã giữ lời hứa.', NULL, 'Tình bạn', 'Biết giữ lời hứa', 'vi', '6-8', 2, 'level_2', 'Ready', 'Manual', false, NULL, NULL, 13, NULL, NOW() - INTERVAL '10 days', false, 'Creator', 'ReadingMediaOnly', 1, 1, md5('seed-creator-story-17'), 'reading'),
    (18, 'Chiếc lá nhỏ', 'Mẫu đang chờ duyệt, dành riêng để test rút yêu cầu', 'Chiếc lá nhỏ rơi xuống hồ. Kiến bò lên lá để qua bờ bên kia. Kiến cảm ơn chiếc lá đã giúp mình.', NULL, 'Tình bạn', 'Biết cảm ơn người giúp mình', 'vi', '6-8', 2, 'level_2', 'Ready', 'Manual', false, NULL, NULL, 13, NULL, NOW() - INTERVAL '1 day', false, 'Creator', 'ReadingMediaOnly', 1, 1, md5('seed-creator-story-18'), 'reading')
ON CONFLICT ("Id") DO NOTHING;

-- Truyện riêng 6-8 tuổi của bé 11 (parent_demo2), nhãn ANIMALS, đã được duyệt lên thư viện cộng đồng (mục 27)
-- để bé 6-8 tuổi (ví dụ bé 1, chỉ cho phép nhãn ANIMALS) thấy sẵn một truyện ở GET /api/v1/CommunityLibrary.
INSERT INTO stories (
    "Id", "Title", "Description", "Content", "CoverImageUrl", "Genre", "MoralLesson", "Language", "AgeBand", "ReadingLevel", "VocabularyLevel", "Status", "Source", "IsPublished", "ChildVisibleAt", "ArchivedReason", "AuthorUserId", "ChildProfileId", "CreatedAt", "IsDeleted", "Scope", "OutputMode", "AudienceRevision", "AuthoringLastVersionNo", "AuthoringRevisionToken", "ExperienceMode"
)
VALUES
    (19, 'Chú voi con tìm bạn', 'Voi con giúp chú khỉ và có thêm một người bạn mới', 'Voi con đi dạo ven suối và gặp chú khỉ đang buồn. Voi con dùng vòi múc nước tưới cho cây chuối của khỉ. Từ hôm đó, hai bạn cùng chăm cây và chơi với nhau mỗi ngày.', NULL, 'Động vật', 'Biết giúp đỡ bạn bè', 'vi', '6-8', 2, 'level_2', 'Ready', 'Ai', true, NOW() - INTERVAL '10 days', NULL, 3, 11, NOW() - INTERVAL '12 days', false, 'Parent', 'Learning', 1, 1, md5('seed-parent-story-19'), 'reading')
ON CONFLICT ("Id") DO NOTHING;

-- Giữ fixture gửi lần đầu ở trạng thái có thể submit kể cả khi seed được chạy lại.
UPDATE stories
SET "Status" = 'Ready', "Description" = 'Mẫu gửi duyệt và rút yêu cầu WF-L5A'
WHERE "Id" = 11 AND "Scope" = 'Creator';

UPDATE stories
SET "PublishedAt" = "ChildVisibleAt"
WHERE "Id" IN (13, 14, 16) AND "IsPublished" = true AND "PublishedAt" IS NULL;

-- Public timestamps are historical demo values; Story.Visibility is not in this schema yet.
UPDATE stories
SET "PublishedAt" = "ChildVisibleAt"
WHERE "Id" IN (1, 3, 6, 10, 19) AND "IsPublished" = true AND "PublishedAt" IS NULL;

UPDATE stories
SET "EverGrayZone" = true
WHERE "Id" IN (4, 6) AND "EverGrayZone" = false;

UPDATE stories
SET "ArchivedReason" = 'SafetyConcern'
WHERE "Id" = 9 AND "Status" = 'Rejected' AND "ArchivedReason" IS NULL;

UPDATE stories
SET "ArchivedAt" = NOW() - INTERVAL '14 days'
WHERE "Id" = 9 AND "Status" = 'Rejected' AND "ArchivedAt" IS NULL;

-- Parent V4 private demo fixture; requires AddParentManualAuthoring. No child access grant.
INSERT INTO stories (
    "Id", "Title", "Content", "Language", "AgeBand", "Status", "Source", "IsPublished",
    "AuthorUserId", "ChildProfileId", "CreatedAt", "IsDeleted", "Scope", "OutputMode",
    "AudienceRevision", "AuthoringLastVersionNo", "AuthoringRevisionToken", "ExperienceMode", "CreateOperationId", "WritingPurpose"
)
VALUES
    (15, 'Chiều cùng con', 'Bắp làm đổ nước. Mẹ cùng Bắp lau bàn. Hai mẹ con mỉm cười.', 'vi', '6-8', 'Draft', 'Manual', false,
     2, 1, '2026-10-18T01:00:00Z', false, 'Parent', 'ReadingMediaOnly', 1, 1, md5('seed-parent-v4-story-15'), 'reading', 'seed-parent-manual-15', 'family_memory')
ON CONFLICT ("Id") DO NOTHING;
