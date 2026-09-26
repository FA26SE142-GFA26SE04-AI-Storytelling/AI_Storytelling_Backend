-- 57. content_reports (5)
INSERT INTO content_reports ("Id", "StoryId", "ReporterUserId", "Reason", "Description", "Status", "ReviewedByUserId", "ReviewedAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 9, 7, 'SafetyConcern', 'Noi dung co the gay so hai cho tre nho', 'ReviewedArchived', 1, NOW() - INTERVAL '14 days', NOW() - INTERVAL '15 days', false),
    (2, 2, 8, 'WrongAgeBand', 'Do kho khong phu hop voi do tuoi 9-12', 'Pending', NULL, NULL, NOW() - INTERVAL '13 days', false),
    (3, 9, 9, 'Inappropriate', 'Mot so tu ngu chua phu hop', 'ReviewedArchived', 1, NOW() - INTERVAL '14 days', NOW() - INTERVAL '14 days', false),
    (4, 4, 10, 'Other', 'Can xem lai thong tin khoa hoc trong truyen', 'ReviewedDismissed', 8, NOW() - INTERVAL '20 days', NOW() - INTERVAL '22 days', false),
    (5, 8, 7, 'SafetyConcern', 'Canh bao ve hanh vi bao luc nhe', 'Pending', NULL, NULL, NOW() - INTERVAL '5 days', false)
ON CONFLICT ("Id") DO NOTHING;
