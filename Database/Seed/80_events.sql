-- 80. events - demo fixtures for the event lifecycle.
INSERT INTO events (
    "Id", "Title", "Slug", "Description", "MainCategoryId", "TargetAgeBands", "Status", "ConfigVersion", "Format", "ScoringMode", "MinVotesPerSubmission", "BudgetVnd", "CreatedByUserId", "AssignedModeratorId", "ApprovedByAdminId", "PublishedAt", "OpensAt", "ClosesAt", "ClosedAt", "ResultsFinalizedAt", "CreatedAt", "IsDeleted"
)
VALUES
    (1001, 'Ngày hội kể chuyện mẫu', 'ngay-hoi-ke-chuyen-mau', 'Sự kiện mẫu đã kết thúc', 1, '6-8', 'Closed', 'ActivityBased', 'Standard', 'ParentVoteAverage', 1, 0, 11, 11, 1, NOW() - INTERVAL '12 days', NOW() - INTERVAL '11 days', NOW() - INTERVAL '2 days', NOW() - INTERVAL '1 days', NOW() - INTERVAL '1 days', NOW() - INTERVAL '12 days', false),
    (1002, 'Bản nháp sáng tác mẫu', 'ban-nhap-sang-tac-mau', 'Bản nháp theo cấu hình cũ', 1, '6-8', 'Draft', 'LegacyRoleBased', 'Standard', 'Rubric', 5, 0, 11, 11, NULL, NULL, NULL, NULL, NULL, NULL, NOW() - INTERVAL '1 days', false)
ON CONFLICT ("Id") DO NOTHING;
