-- 13. story_versions (19: 10 Parent versions, 9 Creator versions)
-- Creator Story 11: versions 11 -> 12 (current); Creator Story 12: version 13 (Import/current).
-- Command receipts and protected preview tokens are created by the API, not fabricated in seed data.
INSERT INTO story_versions (
    "Id", "StoryId", "VersionNo", "Title", "Content", "OutlineOpening", "OutlineDevelopment", "OutlineEnding", "Lesson", "ReadabilityFkgl", "ReadabilityFre", "SafetyScore", "EditType", "EditorUserId", "OutlineApprovedByUserId", "OutlineApprovedAt", "IsCurrent", "CreatedAt", "IsDeleted", "AudienceRevision", "SourceDeclaration", "RightsDeclaration", "AuthoringOrigin", "BaseVersionId", "ImportFileName", "ImportSourceHash"
)
VALUES
    (1, 1, 1, 'Chu Tho Thong Minh - Ban 1', 'Ngay xua, co mot chu tho rat thong minh song trong khu rung...', 'Gioi thieu chu tho va khu rung', 'Chu tho gap nguy hiem va tim cach thoat', 'Chu tho thoat hiem nho su thong minh', 'Su thong minh giup vuot qua kho khan', 3.200, 78.500, 95.000, 'Initial', 2, 2, NOW() - INTERVAL '30 days' + INTERVAL '1 hour', true, NOW() - INTERVAL '30 days', false, 1, '', '', NULL, NULL, NULL, NULL),
    (2, 2, 1, 'Rung Xanh Ky Dieu - Ban 1', 'Trong khu rung xanh co rat nhieu dieu ky dieu dang cho duoc kham pha...', 'Gioi thieu khu rung', 'Hanh trinh kham pha', 'Bai hoc ve thien nhien', 'Tran trong thien nhien', 4.100, 72.300, 90.000, 'Initial', NULL, NULL, NULL, true, NOW() - INTERVAL '28 days', false, 1, '', '', NULL, NULL, NULL, NULL),
    (3, 3, 1, 'Vuong Quoc Co Tich - Ban 1', 'Ngay xua o mot vuong quoc xa xoi...', 'Gioi thieu vuong quoc', 'Thu thach cua nhan vat chinh', 'Long tot chien thang', 'Long tot se duoc den dap', 2.800, 82.000, 97.000, 'Initial', 3, 3, NOW() - INTERVAL '26 days' + INTERVAL '1 hour', true, NOW() - INTERVAL '26 days', false, 1, '', '', NULL, NULL, NULL, NULL),
    (4, 4, 1, 'Hanh Trinh Vu Tru - Ban 1', 'Mot ngay no, ban nho quyet dinh du hanh len vu tru...', 'Gioi thieu chuyen du hanh', 'Kham pha cac hanh tinh', 'Tro ve trai dat voi nhieu bai hoc', 'Kham pha va hoc hoi khong ngung', 4.500, 68.000, 88.000, 'Initial', 8, 3, NOW() - INTERVAL '24 days' + INTERVAL '1 hour', true, NOW() - INTERVAL '24 days', false, 1, '', '', NULL, NULL, NULL, NULL),
    (5, 5, 1, 'Nguoi Ban Tot - Ban 1', 'Hai nguoi ban than thiet cung nhau vuot qua kho khan...', 'Gioi thieu hai nguoi ban', 'Kho khan thu thach tinh ban', 'Tinh ban duoc cung co', 'Tinh ban chan thanh la vo gia', 3.000, 80.000, 96.000, 'Initial', NULL, 4, NOW() - INTERVAL '22 days' + INTERVAL '1 hour', true, NOW() - INTERVAL '22 days', false, 1, '', '', NULL, NULL, NULL, NULL),
    (6, 6, 1, 'Bi Mat Dai Duong - Ban 1', 'Sau lop song bien xanh la ca mot the gioi ky dieu...', 'Gioi thieu the gioi dai duong', 'Kham pha bi mat duoi day bien', 'Bai hoc bao ve moi truong', 'Bao ve moi truong bien', 3.900, 74.000, 92.000, 'Initial', 9, 4, NOW() - INTERVAL '20 days' + INTERVAL '1 hour', true, NOW() - INTERVAL '20 days', false, 1, '', '', NULL, NULL, NULL, NULL),
    (7, 7, 1, 'Gia Dinh Hanh Phuc - Ban 1', 'Moi buoi toi, ca gia dinh quay quan ben nhau...', 'Gioi thieu gia dinh', 'Nhung khoanh khac dang nho', 'Tran trong gia dinh', 'Yeu thuong gia dinh', 2.500, 85.000, 98.000, 'Initial', NULL, NULL, NULL, true, NOW() - INTERVAL '19 days', false, 1, '', '', NULL, NULL, NULL, NULL),
    (8, 8, 1, 'Sieu Nhan Ti Hon - Ban 1', 'Trong mot ngoi lang nho, co mot cau be so huu suc manh dac biet...', 'Gioi thieu sieu nhan ti hon', 'Cuoc chien bao ve xom lang', 'Chien thang cai xau', 'Dung cam bao ve nguoi yeu the', 4.200, 70.000, 89.000, 'Initial', 10, NULL, NULL, true, NOW() - INTERVAL '17 days', false, 1, '', '', NULL, NULL, NULL, NULL),
    (9, 9, 1, 'Chuyen Phieu Luu Rung Ram - Ban 1', 'Bon nguoi ban quyet dinh kham pha khu rung phia sau lang...', 'Gioi thieu nhom ban', 'Thu thach trong rung ram', 'Doan ket vuot kho khan', 'Doan ket tao nen suc manh', 3.600, 76.000, 91.000, 'Initial', NULL, 6, NOW() - INTERVAL '15 days' + INTERVAL '1 hour', false, NOW() - INTERVAL '15 days', false, 1, '', '', NULL, NULL, NULL, NULL),
    (10, 10, 1, 'Tran Bong Da Dang Nho - Ban 1', 'Tran chung ket bong da cua truong dien ra day kich tinh...', 'Gioi thieu tran dau', 'Dien bien gay can', 'Bai hoc ve fair-play', 'Tinh than fair-play trong the thao', 4.000, 73.000, 94.000, 'Initial', 7, 6, NOW() - INTERVAL '13 days' + INTERVAL '1 hour', true, NOW() - INTERVAL '13 days', false, 1, '', '', NULL, NULL, NULL, NULL),
    (11, 11, 1, 'Hạt giống của Lan', 'Lan gieo một hạt giống nhỏ. Mỗi ngày, Lan tưới nước và chờ cây lớn.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'HumanEdited', 13, NULL, NULL, false, NOW() - INTERVAL '2 days', false, 1, 'Nội dung mẫu tự sáng tác để kiểm thử', 'Người kiểm thử có quyền sử dụng nội dung mẫu', 'Write', NULL, NULL, NULL),
    (12, 11, 2, 'Hạt giống của Lan', 'Lan gieo một hạt giống nhỏ. Minh giúp Lan tưới nước mỗi sáng. Khi chiếc lá đầu tiên xuất hiện, hai bạn cùng mỉm cười.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'HumanEdited', 13, NULL, NULL, true, NOW() - INTERVAL '1 day', false, 1, 'Nội dung mẫu tự sáng tác để kiểm thử', 'Người kiểm thử có quyền sử dụng nội dung mẫu', 'Write', 11, NULL, NULL),
    (13, 12, 1, 'Chiếc ô tình bạn', 'Thỏ tìm thấy một chiếc ô bên đường. Khi trời mưa, Thỏ mời Sóc cùng trú dưới ô. Hai bạn vui vẻ đi về nhà.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'HumanEdited', 13, NULL, NULL, true, NOW() - INTERVAL '2 days', false, 1, 'Nội dung mẫu tự sáng tác để kiểm thử', 'Người kiểm thử có quyền sử dụng nội dung mẫu', 'Import', NULL, 'chiec-o-tinh-ban.txt', '73FEF5D8FF880C53B1CB38331E746F013429C04260746C0558C7E746D02473FD')
ON CONFLICT ("Id") DO NOTHING;

-- WF-L5A priority-3 fixtures.
-- Story 13: v1 public, v2 changes requested, v3 is a genuinely changed version ready to resubmit.
-- Story 14: v1 public, v2 changes requested, v3 already pending as a recent resubmission.
INSERT INTO story_versions (
    "Id", "StoryId", "VersionNo", "Title", "Content",
    "OutlineOpening", "OutlineDevelopment", "OutlineEnding", "Lesson",
    "ReadabilityFkgl", "ReadabilityFre", "SafetyScore", "EditType", "EditorUserId",
    "OutlineApprovedByUserId", "OutlineApprovedAt", "IsCurrent", "CreatedAt", "IsDeleted",
    "AudienceRevision", "SourceDeclaration", "RightsDeclaration", "AuthoringOrigin", "BaseVersionId",
    "ImportFileName", "ImportSourceHash", "ReviewStatus", "ReviewSubmittedAt",
    "ReviewedByUserId", "ReviewedAt", "ReviewFeedback"
)
VALUES
    (14, 13, 1, 'Khu vườn biết sẻ chia',
     'Mai trồng một luống hoa nhỏ. Mỗi sáng, em chăm chỉ tưới cây.',
     'Mai bắt đầu trồng hoa', 'Mai chăm sóc khu vườn', 'Hoa nở trong sân trường',
     'Chăm chỉ tạo nên kết quả đẹp', 2.800, 82.000, 98.000, 'Initial', 13,
     NULL, NULL, false, NOW() - INTERVAL '8 days', false,
     1, 'Nội dung mẫu tự sáng tác để kiểm thử', 'Người kiểm thử có quyền sử dụng nội dung mẫu',
     'Write', NULL, NULL, NULL, 'Approved', NOW() - INTERVAL '7 days',
     12, NOW() - INTERVAL '6 days', '{"general":"Đạt yêu cầu công khai"}'::jsonb),
    (15, 13, 2, 'Khu vườn biết sẻ chia',
     'Mai trồng hoa. Nam hái tất cả những bông hoa đẹp nhất.',
     'Mai trồng hoa', 'Nam đến khu vườn', 'Nam hái hết hoa',
     'Cần làm rõ việc xin phép và sẻ chia', 2.600, 84.000, 91.000, 'HumanEdited', 13,
     NULL, NULL, false, NOW() - INTERVAL '3 days', false,
     1, 'Nội dung mẫu tự sáng tác để kiểm thử', 'Người kiểm thử có quyền sử dụng nội dung mẫu',
     'Write', 14, NULL, NULL, 'ChangesRequested', NOW() - INTERVAL '2 days',
     11, NOW() - INTERVAL '6 hours', '{"general":"Bổ sung việc xin phép và kết thúc tích cực"}'::jsonb),
    (16, 13, 3, 'Khu vườn biết sẻ chia',
     'Mai trồng một luống hoa nhỏ. Nam xin phép hái một bông và cùng Mai chăm sóc khu vườn. Hai bạn chia hoa cho lớp học.',
     'Mai trồng một luống hoa nhỏ', 'Nam xin phép và cùng chăm sóc', 'Hai bạn chia hoa cho lớp',
     'Biết xin phép và sẻ chia', 2.900, 81.000, 99.000, 'HumanEdited', 13,
     NULL, NULL, true, NOW() - INTERVAL '2 hours', false,
     1, 'Nội dung mẫu tự sáng tác để kiểm thử', 'Người kiểm thử có quyền sử dụng nội dung mẫu',
     'Write', 15, NULL, NULL, 'None', NULL, NULL, NULL, NULL),
    (17, 14, 1, 'Ngọn đèn trong đêm',
     'An thắp một ngọn đèn nhỏ. Ánh sáng giúp các bạn tìm thấy đường về.',
     'An chuẩn bị ngọn đèn', 'Các bạn nhìn thấy ánh sáng', 'Mọi người về nhà an toàn',
     'Giúp đỡ bạn bè khi gặp khó khăn', 2.700, 83.000, 98.000, 'Initial', 13,
     NULL, NULL, false, NOW() - INTERVAL '9 days', false,
     1, 'Nội dung mẫu tự sáng tác để kiểm thử', 'Người kiểm thử có quyền sử dụng nội dung mẫu',
     'Write', NULL, NULL, NULL, 'Approved', NOW() - INTERVAL '8 days',
     12, NOW() - INTERVAL '7 days', '{"general":"Đạt yêu cầu công khai"}'::jsonb),
    (18, 14, 2, 'Ngọn đèn trong đêm',
     'An đặt ngọn đèn sát rèm cửa. Ngọn lửa bùng lên làm các bạn hoảng sợ.',
     'An đặt ngọn đèn', 'Ngọn lửa bùng lên', 'Các bạn hoảng sợ',
     'Cần sửa chi tiết chưa an toàn', 3.100, 76.000, 80.000, 'HumanEdited', 13,
     NULL, NULL, false, NOW() - INTERVAL '2 days', false,
     1, 'Nội dung mẫu tự sáng tác để kiểm thử', 'Người kiểm thử có quyền sử dụng nội dung mẫu',
     'Write', 17, NULL, NULL, 'ChangesRequested', NOW() - INTERVAL '1 day',
     11, NOW() - INTERVAL '6 hours', '{"general":"Thay chi tiết lửa bằng nguồn sáng an toàn"}'::jsonb),
    (19, 14, 3, 'Ngọn đèn trong đêm',
     'An đặt ngọn đèn an toàn bên cửa sổ. Ánh sáng dịu giúp các bạn tìm thấy đường về. Cả nhóm cảm ơn An.',
     'An đặt đèn an toàn', 'Ánh sáng dẫn đường', 'Cả nhóm cảm ơn An',
     'Bình tĩnh giúp đỡ mọi người', 2.800, 82.000, 99.000, 'HumanEdited', 13,
     NULL, NULL, true, NOW() - INTERVAL '2 hours', false,
     1, 'Nội dung mẫu tự sáng tác để kiểm thử', 'Người kiểm thử có quyền sử dụng nội dung mẫu',
     'Write', 18, NULL, NULL, 'Pending', NOW() - INTERVAL '1 hour', NULL, NULL, NULL)
ON CONFLICT ("Id") DO NOTHING;

-- Story 11 is intentionally reset to an initial-submission state when demo seed is reapplied.
UPDATE story_versions
SET "ReviewStatus" = 'None', "ReviewSubmittedAt" = NULL,
    "ReviewedByUserId" = NULL, "ReviewedAt" = NULL, "ReviewFeedback" = NULL
WHERE "Id" = 12;

UPDATE story_versions
SET "ReviewStatus" = 'Approved', "ReviewSubmittedAt" = NOW() - INTERVAL '30 days' + INTERVAL '1 hour',
    "ReviewedByUserId" = 11, "ReviewedAt" = NOW() - INTERVAL '30 days' + INTERVAL '90 minutes'
WHERE "Id" = 1 AND "ReviewStatus" = 'None';

UPDATE story_versions
SET "ModerationZone" = 'GrayZone', "GrayZoneFlags" = '{"sections":["development"],"reason":"age_band_review"}'::jsonb,
    "ReviewStatus" = 'Pending', "ReviewSubmittedAt" = NOW() - INTERVAL '1 day'
WHERE "Id" = 4 AND "ReviewStatus" = 'None';

UPDATE story_versions
SET "ModerationZone" = 'GrayZone', "GrayZoneFlags" = '{"sections":["ending"],"reason":"safety_review"}'::jsonb,
    "GrayZoneCleared" = true, "GrayZoneClearedByUserId" = 12,
    "GrayZoneClearedAt" = NOW() - INTERVAL '20 days' + INTERVAL '90 minutes',
    "ReviewStatus" = 'Approved', "ReviewSubmittedAt" = NOW() - INTERVAL '20 days' + INTERVAL '1 hour',
    "ReviewedByUserId" = 12, "ReviewedAt" = NOW() - INTERVAL '20 days' + INTERVAL '90 minutes',
    "ReviewFeedback" = '{"ending":"Adjusted wording for age group"}'::jsonb
WHERE "Id" = 6 AND "ReviewStatus" = 'None';

UPDATE story_versions
SET "ReviewStatus" = 'Rejected', "ReviewedByUserId" = 11,
    "ReviewedAt" = NOW() - INTERVAL '14 days',
    "ReviewFeedback" = '{"development":"Safety concern in this section"}'::jsonb
WHERE "Id" = 9 AND "ReviewStatus" = 'None';

-- Parent V4 private demo fixture; requires AddParentManualAuthoring. No child access grant.
INSERT INTO story_versions (
    "Id", "StoryId", "VersionNo", "Title", "Content", "EditType", "EditorUserId", "IsCurrent",
    "CreatedAt", "IsDeleted", "AudienceRevision", "SourceDeclaration", "RightsDeclaration", "AuthoringOrigin"
)
VALUES
    (14, 13, 1, 'Chiều cùng con', 'Bắp làm đổ nước. Mẹ cùng Bắp lau bàn. Hai mẹ con mỉm cười.', 'HumanEdited', 2, true,
     '2026-10-18T01:05:00Z', false, 1, 'Nội dung mẫu tự sáng tác', 'Có quyền sử dụng nội dung mẫu', 'Write')
ON CONFLICT ("Id") DO NOTHING;
