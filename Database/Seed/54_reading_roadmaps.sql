-- 54. reading_roadmaps (6): published, open clone, pending review, archived and draft examples.
-- Schema-only fixtures: current Story rows are child-owned until Public Library fields are added.
INSERT INTO reading_roadmaps (
    "Id", "Name", "Description", "AgeBand", "MainCategoryId", "ReadingLevelMin",
    "ReadingLevelMax", "DefaultDaysPerStory", "Status", "CreatedByUserId", "SubmittedAt",
    "PublishedByUserId", "PublishedAt", "ClonedFromId", "ArchivedReason",
    "ArchivedByUserId", "ArchiveNote", "ArchivedAt", "CreatedAt", "IsDeleted"
)
VALUES
    (1, 'Kham pha dong vat 6-8', 'Bo truyen mau da xuat ban', 'Age_6_8', 1, 1,
     3, 2, 'Published', 11, NOW() - INTERVAL '12 days',
     12, NOW() - INTERVAL '11 days', NULL, NULL,
     NULL, NULL, NULL, NOW() - INTERVAL '14 days', false),
    (2, 'Kham pha dong vat 6-8 - ban sua', 'Ban nhan de chinh sua', 'Age_6_8', 1, 1,
     3, 3, 'Draft', 11, NULL,
     NULL, NULL, 1, NULL,
     NULL, NULL, NULL, NOW() - INTERVAL '2 days', false),
    (3, 'Khoa hoc quanh em 9-10', 'Cho Moderator khac duyet', 'Age_9_10', 4, 3,
     5, 2, 'PendingReview', 12, NOW() - INTERVAL '1 day',
     NULL, NULL, 5, NULL,
     NULL, NULL, NULL, NOW() - INTERVAL '2 days', false),
    (4, 'Kham pha dong vat 6-8 - ban cu', 'Ban nhan truoc da luu tru', 'Age_6_8', 1, 1,
     3, 2, 'Archived', 11, NULL,
     NULL, NULL, 1, 'ReplacedByClone',
     12, 'Thay bang ban nhan moi', NOW() - INTERVAL '3 days', NOW() - INTERVAL '5 days', false),
    (5, 'Khoa hoc quanh em 9-10 - ban da xuat ban', 'Bo khoa hoc gom hai truyen da duyet', 'Age_9_10', 4, 3,
     5, 2, 'Published', 11, NOW() - INTERVAL '10 days',
     12, NOW() - INTERVAL '9 days', NULL, NULL,
     NULL, NULL, NULL, NOW() - INTERVAL '11 days', false),
    (6, 'Co tich cho be 6-8', 'Ban nhap dang sap xep bai doc', 'Age_6_8', 3, 1,
     3, 2, 'Draft', 12, NULL,
     NULL, NULL, NULL, NULL,
     NULL, NULL, NULL, NOW() - INTERVAL '1 day', false)
ON CONFLICT ("Id") DO NOTHING;

-- Report nay nham vao mot truyen trong bo mau; roadmap phai duoc tao truoc khi gan FK.
UPDATE content_reports
SET "ReadingRoadmapId" = 3
WHERE "Id" = 7 AND "ReadingRoadmapId" IS NULL;
