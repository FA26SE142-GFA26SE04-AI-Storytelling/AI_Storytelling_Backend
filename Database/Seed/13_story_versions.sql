-- 13. story_versions (24: 12 Parent versions, 12 Creator versions)
-- Creator Story 11: versions 11 -> 12 (current); Creator Story 12: version 13 (Import/current).
-- Command receipts and protected preview tokens are created by the API, not fabricated in seed data.
INSERT INTO story_versions (
    "Id", "StoryId", "VersionNo", "Title", "Content", "OutlineOpening", "OutlineDevelopment", "OutlineEnding", "Lesson", "ReadabilityFkgl", "ReadabilityFre", "SafetyScore", "EditType", "EditorUserId", "OutlineApprovedByUserId", "OutlineApprovedAt", "IsCurrent", "CreatedAt", "IsDeleted", "AudienceRevision", "BaseVersionId"
)
VALUES
    (1, 1, 1, 'Chú Thỏ Thông Minh - Bản 1', 'Ngày xưa, có một chú thỏ rất thông minh sống trong khu rừng...', 'Giới thiệu chú thỏ và khu rừng', 'Chú thỏ gặp nguy hiểm và tìm cách thoát', 'Chú thỏ thoát hiểm nhờ sự thông minh', 'Sự thông minh giúp vượt qua khó khăn', 3.200, 78.500, 95.000, 'Initial', 2, 2, NOW() - INTERVAL '30 days' + INTERVAL '1 hour', true, NOW() - INTERVAL '30 days', false, 1, NULL),
    (2, 2, 1, 'Rừng Xanh Kỳ Diệu - Bản 1', 'Trong khu rừng xanh có rất nhiều điều kỳ diệu đang chờ được khám phá...', 'Giới thiệu khu rừng', 'Hành trình khám phá', 'Bài học về thiên nhiên', 'Trân trọng thiên nhiên', 4.100, 72.300, 90.000, 'Initial', NULL, NULL, NULL, true, NOW() - INTERVAL '28 days', false, 1, NULL),
    (3, 3, 1, 'Vương Quốc Cổ Tích - Bản 1', 'Ngày xưa ở một vương quốc xa xôi...', 'Giới thiệu vương quốc', 'Thử thách của nhân vật chính', 'Lòng tốt chiến thắng', 'Lòng tốt sẽ được đền đáp', 2.800, 82.000, 97.000, 'Initial', 3, 3, NOW() - INTERVAL '26 days' + INTERVAL '1 hour', true, NOW() - INTERVAL '26 days', false, 1, NULL),
    (4, 4, 1, 'Hành Trình Vũ Trụ - Bản 1', 'Một ngày nọ, bạn nhỏ quyết định du hành lên vũ trụ...', 'Giới thiệu chuyến du hành', 'Khám phá các hành tinh', 'Trở về trái đất với nhiều bài học', 'Khám phá và học hỏi không ngừng', 4.500, 68.000, 88.000, 'Initial', 8, 3, NOW() - INTERVAL '24 days' + INTERVAL '1 hour', true, NOW() - INTERVAL '24 days', false, 1, NULL),
    (5, 5, 1, 'Người Bạn Tốt - Bản 1', 'Hai người bạn thân thiết cùng nhau vượt qua khó khăn...', 'Giới thiệu hai người bạn', 'Khó khăn thử thách tình bạn', 'Tình bạn được củng cố', 'Tình bạn chân thành là vô giá', 3.000, 80.000, 96.000, 'Initial', NULL, 4, NOW() - INTERVAL '22 days' + INTERVAL '1 hour', true, NOW() - INTERVAL '22 days', false, 1, NULL),
    (6, 6, 1, 'Bí Mật Đại Dương - Bản 1', 'Sau lớp sóng biển xanh là cả một thế giới kỳ diệu...', 'Giới thiệu thế giới đại dương', 'Khám phá bí mật dưới đáy biển', 'Bài học bảo vệ môi trường', 'Bảo vệ môi trường biển', 3.900, 74.000, 92.000, 'Initial', 9, 4, NOW() - INTERVAL '20 days' + INTERVAL '1 hour', true, NOW() - INTERVAL '20 days', false, 1, NULL),
    (7, 7, 1, 'Gia Đình Hạnh Phúc - Bản 1', 'Mỗi buổi tối, cả gia đình quây quần bên nhau...', 'Giới thiệu gia đình', 'Những khoảnh khắc đáng nhớ', 'Trân trọng gia đình', 'Yêu thương gia đình', 2.500, 85.000, 98.000, 'Initial', NULL, NULL, NULL, true, NOW() - INTERVAL '19 days', false, 1, NULL),
    (8, 8, 1, 'Siêu Nhân Tí Hon - Bản 1', 'Trong một ngôi làng nhỏ, có một cậu bé sở hữu sức mạnh đặc biệt...', 'Giới thiệu siêu nhân tí hon', 'Cuộc chiến bảo vệ xóm làng', 'Chiến thắng cái xấu', 'Dũng cảm bảo vệ người yếu thế', 4.200, 70.000, 89.000, 'Initial', 10, NULL, NULL, true, NOW() - INTERVAL '17 days', false, 1, NULL),
    (9, 9, 1, 'Chuyến Phiêu Lưu Rừng Rậm - Bản 1', 'Bốn người bạn quyết định khám phá khu rừng phía sau làng...', 'Giới thiệu nhóm bạn', 'Thử thách trong rừng rậm', 'Đoàn kết vượt khó khăn', 'Đoàn kết tạo nên sức mạnh', 3.600, 76.000, 91.000, 'Initial', NULL, 6, NOW() - INTERVAL '15 days' + INTERVAL '1 hour', false, NOW() - INTERVAL '15 days', false, 1, NULL),
    (10, 10, 1, 'Trận Bóng Đá Đáng Nhớ - Bản 1', 'Trận chung kết bóng đá của trường diễn ra đầy kịch tính...', 'Giới thiệu trận đấu', 'Diễn biến gay cấn', 'Bài học về fair-play', 'Tinh thần fair-play trong thể thao', 4.000, 73.000, 94.000, 'Initial', 7, 6, NOW() - INTERVAL '13 days' + INTERVAL '1 hour', true, NOW() - INTERVAL '13 days', false, 1, NULL),
    (11, 11, 1, 'Hạt giống của Lan', 'Lan gieo một hạt giống nhỏ. Mỗi ngày, Lan tưới nước và chờ cây lớn.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'HumanEdited', 13, NULL, NULL, false, NOW() - INTERVAL '2 days', false, 1, NULL),
    (12, 11, 2, 'Hạt giống của Lan', 'Lan gieo một hạt giống nhỏ. Minh giúp Lan tưới nước mỗi sáng. Khi chiếc lá đầu tiên xuất hiện, hai bạn cùng mỉm cười.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'HumanEdited', 13, NULL, NULL, true, NOW() - INTERVAL '1 day', false, 1, 11),
    (13, 12, 1, 'Chiếc ô tình bạn', 'Thỏ tìm thấy một chiếc ô bên đường. Khi trời mưa, Thỏ mời Sóc cùng trú dưới ô. Hai bạn vui vẻ đi về nhà.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'HumanEdited', 13, NULL, NULL, true, NOW() - INTERVAL '2 days', false, 1, NULL)
ON CONFLICT ("Id") DO NOTHING;

-- WF-L5A priority-3 fixtures.
-- Story 13: v1 public, v2 changes requested, v3 is a genuinely changed version ready to resubmit.
-- Story 14: v1 public, v2 changes requested, v3 already pending as a recent resubmission.
-- Story 16: v1 (Id 21) public and current; used for removal request -> decision -> revisions.
-- Story 17: v1 (Id 22) approved, then removed by a moderator; revisions from 22 succeeds directly.
-- Story 18: v1 (Id 23) submitted and still Pending (item 26); used to test creator withdraw.
INSERT INTO story_versions (
    "Id", "StoryId", "VersionNo", "Title", "Content", "OutlineOpening", "OutlineDevelopment", "OutlineEnding", "Lesson", "ReadabilityFkgl", "ReadabilityFre", "SafetyScore", "EditType", "EditorUserId", "OutlineApprovedByUserId", "OutlineApprovedAt", "IsCurrent", "CreatedAt", "IsDeleted", "AudienceRevision", "BaseVersionId"
)
VALUES
    (14, 13, 1, 'Khu vườn biết sẻ chia', 'Mai trồng một luống hoa nhỏ. Mỗi sáng, em chăm chỉ tưới cây.', 'Mai bắt đầu trồng hoa', 'Mai chăm sóc khu vườn', 'Hoa nở trong sân trường', 'Chăm chỉ tạo nên kết quả đẹp', 2.800, 82.000, 98.000, 'Initial', 13, NULL, NULL, false, NOW() - INTERVAL '8 days', false, 1, NULL),
    (15, 13, 2, 'Khu vườn biết sẻ chia', 'Mai trồng hoa. Nam hái tất cả những bông hoa đẹp nhất.', 'Mai trồng hoa', 'Nam đến khu vườn', 'Nam hái hết hoa', 'Cần làm rõ việc xin phép và sẻ chia', 2.600, 84.000, 91.000, 'HumanEdited', 13, NULL, NULL, false, NOW() - INTERVAL '3 days', false, 1, 14),
    (16, 13, 3, 'Khu vườn biết sẻ chia', 'Mai trồng một luống hoa nhỏ. Nam xin phép hái một bông và cùng Mai chăm sóc khu vườn. Hai bạn chia hoa cho lớp học.', 'Mai trồng một luống hoa nhỏ', 'Nam xin phép và cùng chăm sóc', 'Hai bạn chia hoa cho lớp', 'Biết xin phép và sẻ chia', 2.900, 81.000, 99.000, 'HumanEdited', 13, NULL, NULL, true, NOW() - INTERVAL '2 hours', false, 1, 15),
    (17, 14, 1, 'Ngọn đèn trong đêm', 'An thắp một ngọn đèn nhỏ. Ánh sáng giúp các bạn tìm thấy đường về.', 'An chuẩn bị ngọn đèn', 'Các bạn nhìn thấy ánh sáng', 'Mọi người về nhà an toàn', 'Giúp đỡ bạn bè khi gặp khó khăn', 2.700, 83.000, 98.000, 'Initial', 13, NULL, NULL, false, NOW() - INTERVAL '9 days', false, 1, NULL),
    (18, 14, 2, 'Ngọn đèn trong đêm', 'An đặt ngọn đèn sát rèm cửa. Ngọn lửa bùng lên làm các bạn hoảng sợ.', 'An đặt ngọn đèn', 'Ngọn lửa bùng lên', 'Các bạn hoảng sợ', 'Cần sửa chi tiết chưa an toàn', 3.100, 76.000, 80.000, 'HumanEdited', 13, NULL, NULL, false, NOW() - INTERVAL '2 days', false, 1, 17),
    (19, 14, 3, 'Ngọn đèn trong đêm', 'An đặt ngọn đèn an toàn bên cửa sổ. Ánh sáng dịu giúp các bạn tìm thấy đường về. Cả nhóm cảm ơn An.', 'An đặt đèn an toàn', 'Ánh sáng dẫn đường', 'Cả nhóm cảm ơn An', 'Bình tĩnh giúp đỡ mọi người', 2.800, 82.000, 99.000, 'HumanEdited', 13, NULL, NULL, true, NOW() - INTERVAL '2 hours', false, 1, 18),
    (21, 16, 1, 'Chú mèo biết lắng nghe', 'Mèo Mun ngồi nghe bạn Gà kể chuyện. Mun không ngắt lời và chờ bạn nói xong. Gà rất vui vì có một người bạn biết lắng nghe.', 'Gà muốn kể chuyện', 'Mun chăm chú lắng nghe', 'Gà vui vì được lắng nghe', 'Biết lắng nghe bạn bè', 2.700, 83.000, 98.000, 'Initial', 13, NULL, NULL, true, NOW() - INTERVAL '6 days', false, 1, NULL),
    (22, 17, 1, 'Bạn Rùa đúng hẹn', 'Rùa hẹn Thỏ đến sân chơi lúc chiều. Trời mưa nhỏ nhưng Rùa vẫn mang ô đến đúng giờ. Thỏ cảm ơn Rùa vì đã giữ lời hứa.', 'Rùa hẹn gặp Thỏ', 'Rùa đến đúng giờ dù trời mưa', 'Thỏ cảm ơn Rùa', 'Biết giữ lời hứa', 2.600, 84.000, 98.000, 'Initial', 13, NULL, NULL, true, NOW() - INTERVAL '10 days', false, 1, NULL),
    (23, 18, 1, 'Chiếc lá nhỏ', 'Chiếc lá nhỏ rơi xuống hồ. Kiến bò lên lá để qua bờ bên kia. Kiến cảm ơn chiếc lá đã giúp mình.', 'Chiếc lá rơi xuống hồ', 'Kiến nhờ lá qua bờ', 'Kiến cảm ơn chiếc lá', 'Biết cảm ơn người giúp mình', 2.500, 85.000, 98.000, 'Initial', 13, NULL, NULL, true, NOW() - INTERVAL '1 day', false, 1, NULL)
ON CONFLICT ("Id") DO NOTHING;

-- Story 19: v1 (Id 24) is the version pinned by Community item 27 (Approved).
INSERT INTO story_versions (
    "Id", "StoryId", "VersionNo", "Title", "Content", "OutlineOpening", "OutlineDevelopment", "OutlineEnding", "Lesson", "ReadabilityFkgl", "ReadabilityFre", "SafetyScore", "EditType", "EditorUserId", "OutlineApprovedByUserId", "OutlineApprovedAt", "IsCurrent", "CreatedAt", "IsDeleted", "AudienceRevision", "BaseVersionId"
)
VALUES
    (24, 19, 1, 'Chú voi con tìm bạn', 'Voi con đi dạo ven suối và gặp chú khỉ đang buồn. Voi con dùng vòi múc nước tưới cho cây chuối của khỉ. Từ hôm đó, hai bạn cùng chăm cây và chơi với nhau mỗi ngày.', 'Voi con đi dạo ven suối', 'Voi con giúp khỉ tưới cây chuối', 'Hai bạn cùng chăm cây mỗi ngày', 'Biết giúp đỡ bạn bè', 2.400, 86.000, 98.000, 'Initial', NULL, 3, NOW() - INTERVAL '12 days' + INTERVAL '1 hour', true, NOW() - INTERVAL '12 days', false, 1, NULL)
ON CONFLICT ("Id") DO NOTHING;

-- Story 11 is intentionally reset to an initial-submission state when demo seed is reapplied.










-- Parent V4 private demo fixture; requires AddParentManualAuthoring. No child access grant.
INSERT INTO story_versions (
    "Id", "StoryId", "VersionNo", "Title", "Content", "EditType", "EditorUserId", "IsCurrent", "CreatedAt", "IsDeleted", "AudienceRevision"
)
VALUES
    (20, 15, 1, 'Chiều cùng con', 'Bắp làm đổ nước. Mẹ cùng Bắp lau bàn. Hai mẹ con mỉm cười.', 'HumanEdited', 2, true, '2026-10-18T01:05:00Z', false, 1)
ON CONFLICT ("Id") DO NOTHING;
