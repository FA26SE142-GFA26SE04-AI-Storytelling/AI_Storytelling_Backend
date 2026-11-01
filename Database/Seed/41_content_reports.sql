-- 41. content_reports (7)
INSERT INTO content_reports (
    "Id", "StoryId", "StoryVersionId", "ReporterUserId", "Reason", "Description",
    "Status", "IsReviewItem", "AttachmentUrl", "ReviewedByUserId", "ReviewedAt",
    "ResolvedAt", "CreatedAt", "IsDeleted"
)
VALUES
    (1, 9, 9, 7, 'SafetyConcern', 'Nội dung có thể gây sợ hãi cho trẻ nhỏ', 'ReviewedArchived', false, NULL,
     11, NOW() - INTERVAL '14 days', NOW() - INTERVAL '14 days', NOW() - INTERVAL '15 days', false),
    (2, 2, 2, 8, 'WrongAgeBand', 'Độ khó không phù hợp với độ tuổi 9-10', 'Pending', false, NULL,
     NULL, NULL, NULL, NOW() - INTERVAL '13 days', false),
    (3, 9, 9, 9, 'Inappropriate', 'Một số từ ngữ chưa phù hợp', 'ReviewedArchived', false, NULL,
     11, NOW() - INTERVAL '14 days', NOW() - INTERVAL '14 days', NOW() - INTERVAL '14 days', false),
    (4, 4, 4, 10, 'Other', 'Cần xem lại thông tin khoa học trong truyện', 'ReviewedDismissed', false, NULL,
     12, NOW() - INTERVAL '20 days', NOW() - INTERVAL '20 days', NOW() - INTERVAL '22 days', false),
    (5, 8, 8, 7, 'SafetyConcern', 'Cảnh báo về hành vi bạo lực nhẹ', 'Pending', false, NULL,
     NULL, NULL, NULL, NOW() - INTERVAL '5 days', false),
    (6, 6, 6, 8, 'Copyright', 'Cần đối chiếu nguồn minh họa', 'Escalated', false,
     'https://example.com/evidence/story6-source', 11, NOW() - INTERVAL '4 hours', NULL,
     NOW() - INTERVAL '6 hours', false),
    (7, 10, 10, 12, 'Cultural', 'Rà soat cach dung tu trong bo truyen mau', 'Closed', true, NULL,
     11, NOW() - INTERVAL '3 hours', NOW() - INTERVAL '3 hours', NOW() - INTERVAL '5 hours', false)
ON CONFLICT ("Id") DO NOTHING;
