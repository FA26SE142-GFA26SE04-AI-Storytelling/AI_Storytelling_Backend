-- 47. content_reports (7)
INSERT INTO content_reports (
    "Id", "StoryId", "StoryVersionId", "ReporterUserId", "Reason", "Description",
    "Status", "IsReviewItem", "AttachmentUrl", "ReviewedByUserId", "ReviewedAt",
    "ResolvedAt", "CreatedAt", "IsDeleted"
)
VALUES
    (1, 9, 9, 7, 'SafetyConcern', 'Noi dung co the gay so hai cho tre nho', 'ReviewedArchived', false, NULL,
     11, NOW() - INTERVAL '14 days', NOW() - INTERVAL '14 days', NOW() - INTERVAL '15 days', false),
    (2, 2, 2, 8, 'WrongAgeBand', 'Do kho khong phu hop voi do tuoi 9-10', 'Pending', false, NULL,
     NULL, NULL, NULL, NOW() - INTERVAL '13 days', false),
    (3, 9, 9, 9, 'Inappropriate', 'Mot so tu ngu chua phu hop', 'ReviewedArchived', false, NULL,
     11, NOW() - INTERVAL '14 days', NOW() - INTERVAL '14 days', NOW() - INTERVAL '14 days', false),
    (4, 4, 4, 10, 'Other', 'Can xem lai thong tin khoa hoc trong truyen', 'ReviewedDismissed', false, NULL,
     12, NOW() - INTERVAL '20 days', NOW() - INTERVAL '20 days', NOW() - INTERVAL '22 days', false),
    (5, 8, 8, 7, 'SafetyConcern', 'Canh bao ve hanh vi bao luc nhe', 'Pending', false, NULL,
     NULL, NULL, NULL, NOW() - INTERVAL '5 days', false),
    (6, 6, 6, 8, 'Copyright', 'Can doi chieu nguon minh hoa', 'Escalated', false,
     'https://example.com/evidence/story6-source', 11, NOW() - INTERVAL '4 hours', NULL,
     NOW() - INTERVAL '6 hours', false),
    (7, 10, 10, 12, 'Cultural', 'Rà soat cach dung tu trong bo truyen mau', 'Closed', true, NULL,
     11, NOW() - INTERVAL '3 hours', NOW() - INTERVAL '3 hours', NOW() - INTERVAL '5 hours', false)
ON CONFLICT ("Id") DO NOTHING;
