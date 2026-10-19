-- 11. stories (14: 10 Parent, 4 Creator)
-- Creator: Id 11 = manual V2, Id 12 = imported text V1; author is creator_demo (Id 13).
-- WF-L5A fixtures: Id 11 = first submit/withdraw; Id 13 = creator resubmit;
-- Id 14 = recent pending resubmission for moderator queue/history/compare.
-- ON CONFLICT preserves existing rows; this file does not overwrite live authoring data.
INSERT INTO stories (
    "Id", "Title", "Description", "Content", "CoverImageUrl", "Genre", "MoralLesson", "Language", "AgeBand", "ReadingLevel", "VocabularyLevel", "Status", "Source", "IsPublished", "ChildVisibleAt", "ArchivedReason", "AuthorUserId", "ChildProfileId", "CreatedAt", "IsDeleted", "Scope", "OutputMode", "AudienceRevision", "AuthoringLastVersionNo", "AuthoringRevisionToken", "ExperienceMode"
)
VALUES
    (1, 'Chu Tho Thong Minh', 'Cau chuyen ve chu tho biet dung tri thong minh de thoat hiem', 'Ngay xua, co mot chu tho rat thong minh song trong khu rung...', 'https://example.com/covers/story1.png', 'Ngu ngon', 'Su thong minh giup vuot qua kho khan', 'vi', '6-8', 2, 'level_2', 'Ready', 'Manual', true, NOW() - INTERVAL '30 days' + INTERVAL '2 hours', NULL, 2, 1, NOW() - INTERVAL '30 days', false, 'Parent', 'Learning', 1, 1, md5('seed-parent-story-1'), 'reading'),
    (2, 'Rung Xanh Ky Dieu', 'Hanh trinh kham pha khu rung day mau sac', 'Trong khu rung xanh co rat nhieu dieu ky dieu dang cho duoc kham pha...', 'https://example.com/covers/story2.png', 'Phieu luu', 'Tran trong thien nhien', 'vi', '9-10', 4, 'level_4', 'Draft', 'Ai', false, NULL, NULL, 7, 2, NOW() - INTERVAL '28 days', false, 'Parent', 'Learning', 1, 1, md5('seed-parent-story-2'), 'reading'),
    (3, 'Vuong Quoc Co Tich', 'Mot vuong quoc noi phep thuat va long tot ngu tri', 'Ngay xua o mot vuong quoc xa xoi...', 'https://example.com/covers/story3.png', 'Co tich', 'Long tot se duoc den dap', 'vi', '6-8', 2, 'level_2', 'Ready', 'Manual', true, NOW() - INTERVAL '26 days' + INTERVAL '2 hours', NULL, 3, 3, NOW() - INTERVAL '26 days', false, 'Parent', 'Learning', 1, 1, md5('seed-parent-story-3'), 'reading'),
    (4, 'Hanh Trinh Vu Tru', 'Chuyen phieu luu cua mot nha du hanh nho tuoi', 'Mot ngay no, ban nho quyet dinh du hanh len vu tru...', 'https://example.com/covers/story4.png', 'Khoa hoc vien tuong', 'Kham pha va hoc hoi khong ngung', 'vi', '9-10', 5, 'level_5', 'ContentReview', 'Ai', false, NULL, NULL, 8, 4, NOW() - INTERVAL '24 days', false, 'Parent', 'Learning', 1, 1, md5('seed-parent-story-4'), 'reading'),
    (5, 'Nguoi Ban Tot', 'Cau chuyen ve tinh ban chan thanh giua hai dua tre', 'Hai nguoi ban than thiet cung nhau vuot qua kho khan...', 'https://example.com/covers/story5.png', 'Doi thuong', 'Tinh ban chan thanh la vo gia', 'vi', '6-8', 3, 'level_3', 'Approved', 'Manual', false, NOW() - INTERVAL '22 days' + INTERVAL '2 hours', NULL, 4, 5, NOW() - INTERVAL '22 days', false, 'Parent', 'Learning', 1, 1, md5('seed-parent-story-5'), 'reading'),
    (6, 'Bi Mat Dai Duong', 'Kham pha the gioi ky dieu duoi day dai duong', 'Sau lop song bien xanh la ca mot the gioi ky dieu...', 'https://example.com/covers/story6.png', 'Phieu luu', 'Bao ve moi truong bien', 'vi', '9-10', 4, 'level_4', 'Ready', 'Ai', true, NOW() - INTERVAL '20 days' + INTERVAL '2 hours', NULL, 9, 6, NOW() - INTERVAL '20 days', false, 'Parent', 'Learning', 1, 1, md5('seed-parent-story-6'), 'reading'),
    (7, 'Gia Dinh Hanh Phuc', 'Nhung khoanh khac am ap trong mot gia dinh nho', 'Moi buoi toi, ca gia dinh quay quan ben nhau...', 'https://example.com/covers/story7.png', 'Doi thuong', 'Yeu thuong gia dinh', 'vi', '6-8', 2, 'level_2', 'Draft', 'Manual', false, NULL, NULL, 5, 7, NOW() - INTERVAL '19 days', false, 'Parent', 'Learning', 1, 1, md5('seed-parent-story-7'), 'reading'),
    (8, 'Sieu Nhan Ti Hon', 'Cau be co suc manh dac biet bao ve xom lang', 'Trong mot ngoi lang nho, co mot cau be so huu suc manh dac biet...', 'https://example.com/covers/story8.png', 'Sieu anh hung', 'Dung cam bao ve nguoi yeu the', 'vi', '9-10', 5, 'level_5', 'OutlineReview', 'Ai', false, NULL, NULL, 10, 8, NOW() - INTERVAL '17 days', false, 'Parent', 'Learning', 1, 1, md5('seed-parent-story-8'), 'reading'),
    (9, 'Chuyen Phieu Luu Rung Ram', 'Nhom ban cung nhau kham pha khu rung bi an', 'Bon nguoi ban quyet dinh kham pha khu rung phia sau lang...', 'https://example.com/covers/story9.png', 'Phieu luu', 'Doan ket tao nen suc manh', 'vi', '6-8', 3, 'level_3', 'Archived', 'Manual', false, NULL, 'SafetyConcern', 6, 9, NOW() - INTERVAL '15 days', false, 'Parent', 'Learning', 1, 1, md5('seed-parent-story-9'), 'reading'),
    (10, 'Tran Bong Da Dang Nho', 'Bai hoc ve tinh than the thao va fair-play', 'Tran chung ket bong da cua truong dien ra day kich tinh...', 'https://example.com/covers/story10.png', 'The thao', 'Tinh than fair-play trong the thao', 'vi', '9-10', 4, 'level_4', 'Ready', 'Ai', true, NOW() - INTERVAL '13 days' + INTERVAL '2 hours', NULL, 7, 10, NOW() - INTERVAL '13 days', false, 'Parent', 'Learning', 1, 1, md5('seed-parent-story-10'), 'reading'),
    (11, 'Hạt giống của Lan', 'Mẫu gửi duyệt và rút yêu cầu WF-L5A', 'Lan gieo một hạt giống nhỏ. Minh giúp Lan tưới nước mỗi sáng. Khi chiếc lá đầu tiên xuất hiện, hai bạn cùng mỉm cười.', NULL, 'Tình bạn', NULL, 'vi', '6-8', NULL, NULL, 'Ready', 'Manual', false, NULL, NULL, 13, NULL, NOW() - INTERVAL '2 days', false, 'Creator', 'ReadingMediaOnly', 1, 2, md5('seed-creator-story-11'), 'reading'),
    (12, 'Chiếc ô tình bạn', 'Mẫu kiểm thử Creator trên Story/StoryVersion dùng chung', 'Thỏ tìm thấy một chiếc ô bên đường. Khi trời mưa, Thỏ mời Sóc cùng trú dưới ô. Hai bạn vui vẻ đi về nhà.', NULL, 'Tình bạn', NULL, 'vi', '6-8', NULL, NULL, 'Draft', 'Manual', false, NULL, NULL, 13, NULL, NOW() - INTERVAL '2 days', false, 'Creator', 'ReadingMediaOnly', 1, 1, md5('seed-creator-story-12'), 'reading'),
    (13, 'Khu vườn biết sẻ chia', 'Mẫu Creator gửi lại sau yêu cầu sửa WF-L5A', 'Mai trồng một luống hoa nhỏ. Nam xin phép hái một bông và cùng Mai chăm sóc khu vườn. Hai bạn chia hoa cho lớp học.', NULL, 'Tình bạn', 'Biết xin phép và sẻ chia', 'vi', '6-8', 2, 'level_2', 'Ready', 'Manual', true, NOW() - INTERVAL '6 days', NULL, 13, NULL, NOW() - INTERVAL '8 days', false, 'Creator', 'ReadingMediaOnly', 1, 3, md5('seed-creator-story-13'), 'reading'),
    (14, 'Ngọn đèn trong đêm', 'Mẫu gửi lại đang chờ để Moderator kiểm tra WF-L5A', 'An đặt ngọn đèn an toàn bên cửa sổ. Ánh sáng dịu giúp các bạn tìm thấy đường về. Cả nhóm cảm ơn An.', NULL, 'Tình bạn', 'Bình tĩnh giúp đỡ mọi người', 'vi', '6-8', 2, 'level_2', 'Ready', 'Manual', true, NOW() - INTERVAL '7 days', NULL, 13, NULL, NOW() - INTERVAL '9 days', false, 'Creator', 'ReadingMediaOnly', 1, 3, md5('seed-creator-story-14'), 'reading')
ON CONFLICT ("Id") DO NOTHING;

-- Giữ fixture gửi lần đầu ở trạng thái có thể submit kể cả khi seed được chạy lại.
UPDATE stories
SET "Status" = 'Ready', "Description" = 'Mẫu gửi duyệt và rút yêu cầu WF-L5A'
WHERE "Id" = 11 AND "Scope" = 'Creator';

UPDATE stories
SET "PublishedAt" = "ChildVisibleAt"
WHERE "Id" IN (13, 14) AND "IsPublished" = true AND "PublishedAt" IS NULL;

-- Public timestamps are historical demo values; Story.Visibility is not in this schema yet.
UPDATE stories
SET "PublishedAt" = "ChildVisibleAt"
WHERE "Id" IN (1, 3, 6, 10) AND "IsPublished" = true AND "PublishedAt" IS NULL;

UPDATE stories
SET "EverGrayZone" = true
WHERE "Id" IN (4, 6) AND "EverGrayZone" = false;

UPDATE stories
SET "Status" = 'Archived', "ArchivedReason" = 'SafetyConcern'
WHERE "Id" = 9 AND "Status" = 'Rejected';

UPDATE stories
SET "ArchivedAt" = NOW() - INTERVAL '14 days'
WHERE "Id" = 9 AND "Status" = 'Archived' AND "ArchivedAt" IS NULL;

-- Parent V4 private demo fixture; requires AddParentManualAuthoring. No child access grant.
INSERT INTO stories (
    "Id", "Title", "Content", "Language", "AgeBand", "Status", "Source", "IsPublished",
    "AuthorUserId", "ChildProfileId", "CreatedAt", "IsDeleted", "Scope", "OutputMode",
    "AudienceRevision", "AuthoringLastVersionNo", "AuthoringRevisionToken", "ExperienceMode", "CreateOperationId", "WritingPurpose"
)
VALUES
    (13, 'Chiều cùng con', 'Bắp làm đổ nước. Mẹ cùng Bắp lau bàn. Hai mẹ con mỉm cười.', 'vi', '6-8', 'Draft', 'Manual', false,
     2, 1, '2026-10-18T01:00:00Z', false, 'Parent', 'ReadingMediaOnly', 1, 1, md5('seed-parent-v4-story-13'), 'reading', 'seed-parent-manual-13', 'family_memory')
ON CONFLICT ("Id") DO NOTHING;
