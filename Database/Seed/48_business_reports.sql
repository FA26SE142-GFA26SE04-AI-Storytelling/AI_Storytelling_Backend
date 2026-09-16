-- 48. business_reports (10 - 10 thang gan nhat)
INSERT INTO business_reports ("Id", "PeriodStart", "PeriodEnd", "StoriesGenerated", "StoriesApproved", "StoriesRejected", "ReadingSessionsCompleted", "Status", "PublishedAt", "CreatedAt", "IsDeleted")
VALUES
    (1, DATE_TRUNC('month', NOW()) - INTERVAL '10 month', DATE_TRUNC('month', NOW()) - INTERVAL '9 month' - INTERVAL '1 day', 12, 10, 2, 40, 'Published', DATE_TRUNC('month', NOW()) - INTERVAL '9 month', DATE_TRUNC('month', NOW()) - INTERVAL '9 month', false),
    (2, DATE_TRUNC('month', NOW()) - INTERVAL '9 month', DATE_TRUNC('month', NOW()) - INTERVAL '8 month' - INTERVAL '1 day', 15, 12, 3, 55, 'Published', DATE_TRUNC('month', NOW()) - INTERVAL '8 month', DATE_TRUNC('month', NOW()) - INTERVAL '8 month', false),
    (3, DATE_TRUNC('month', NOW()) - INTERVAL '8 month', DATE_TRUNC('month', NOW()) - INTERVAL '7 month' - INTERVAL '1 day', 18, 15, 3, 62, 'Published', DATE_TRUNC('month', NOW()) - INTERVAL '7 month', DATE_TRUNC('month', NOW()) - INTERVAL '7 month', false),
    (4, DATE_TRUNC('month', NOW()) - INTERVAL '7 month', DATE_TRUNC('month', NOW()) - INTERVAL '6 month' - INTERVAL '1 day', 20, 16, 4, 70, 'Published', DATE_TRUNC('month', NOW()) - INTERVAL '6 month', DATE_TRUNC('month', NOW()) - INTERVAL '6 month', false),
    (5, DATE_TRUNC('month', NOW()) - INTERVAL '6 month', DATE_TRUNC('month', NOW()) - INTERVAL '5 month' - INTERVAL '1 day', 22, 18, 4, 75, 'Published', DATE_TRUNC('month', NOW()) - INTERVAL '5 month', DATE_TRUNC('month', NOW()) - INTERVAL '5 month', false),
    (6, DATE_TRUNC('month', NOW()) - INTERVAL '5 month', DATE_TRUNC('month', NOW()) - INTERVAL '4 month' - INTERVAL '1 day', 25, 20, 5, 80, 'Published', DATE_TRUNC('month', NOW()) - INTERVAL '4 month', DATE_TRUNC('month', NOW()) - INTERVAL '4 month', false),
    (7, DATE_TRUNC('month', NOW()) - INTERVAL '4 month', DATE_TRUNC('month', NOW()) - INTERVAL '3 month' - INTERVAL '1 day', 24, 19, 5, 78, 'Published', DATE_TRUNC('month', NOW()) - INTERVAL '3 month', DATE_TRUNC('month', NOW()) - INTERVAL '3 month', false),
    (8, DATE_TRUNC('month', NOW()) - INTERVAL '3 month', DATE_TRUNC('month', NOW()) - INTERVAL '2 month' - INTERVAL '1 day', 28, 23, 5, 90, 'Published', DATE_TRUNC('month', NOW()) - INTERVAL '2 month', DATE_TRUNC('month', NOW()) - INTERVAL '2 month', false),
    (9, DATE_TRUNC('month', NOW()) - INTERVAL '2 month', DATE_TRUNC('month', NOW()) - INTERVAL '1 month' - INTERVAL '1 day', 30, 25, 5, 95, 'Published', DATE_TRUNC('month', NOW()) - INTERVAL '1 month', DATE_TRUNC('month', NOW()) - INTERVAL '1 month', false),
    (10, DATE_TRUNC('month', NOW()) - INTERVAL '1 month', DATE_TRUNC('month', NOW()) - INTERVAL '1 day', 10, 8, 2, 50, 'Compiling', NULL, NOW() - INTERVAL '1 day', false)
ON CONFLICT ("Id") DO NOTHING;
