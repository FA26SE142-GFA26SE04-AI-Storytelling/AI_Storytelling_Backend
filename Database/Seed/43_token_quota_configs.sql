-- 43. token_quota_configs (10) - scope System / Child / Personal (khong con Organization).
INSERT INTO token_quota_configs ("Id", "Scope", "UserId", "ChildProfileId", "QuotaLimit", "QuotaUsed", "PeriodStart", "PeriodEnd", "CreatedAt", "IsDeleted")
VALUES
    (1, 'System', NULL, NULL, 10000, 3200, DATE_TRUNC('month', NOW())::date, (DATE_TRUNC('month', NOW()) + INTERVAL '1 month' - INTERVAL '1 day')::date, NOW() - INTERVAL '10 days', false),
    (4, 'Child', NULL, 1, 50, 12, DATE_TRUNC('month', NOW())::date, (DATE_TRUNC('month', NOW()) + INTERVAL '1 month' - INTERVAL '1 day')::date, NOW() - INTERVAL '10 days', false),
    (5, 'Child', NULL, 9, 50, 50, DATE_TRUNC('month', NOW())::date, (DATE_TRUNC('month', NOW()) + INTERVAL '1 month' - INTERVAL '1 day')::date, NOW() - INTERVAL '10 days', false),
    (6, 'Personal', 2, NULL, 100, 18, DATE_TRUNC('month', NOW())::date, (DATE_TRUNC('month', NOW()) + INTERVAL '1 month' - INTERVAL '1 day')::date, NOW() - INTERVAL '8 days', false),
    (7, 'Child', NULL, 3, 50, 0, DATE_TRUNC('month', NOW())::date, (DATE_TRUNC('month', NOW()) + INTERVAL '1 month' - INTERVAL '1 day')::date, NOW() - INTERVAL '7 days', false),
    (8, 'Child', NULL, 6, 50, 33, DATE_TRUNC('month', NOW())::date, (DATE_TRUNC('month', NOW()) + INTERVAL '1 month' - INTERVAL '1 day')::date, NOW() - INTERVAL '7 days', false),
    (9, 'Child', NULL, 12, 50, 49, DATE_TRUNC('month', NOW())::date, (DATE_TRUNC('month', NOW()) + INTERVAL '1 month' - INTERVAL '1 day')::date, NOW() - INTERVAL '6 days', false),
    (10, 'Personal', 3, NULL, 100, 0, DATE_TRUNC('month', NOW())::date, (DATE_TRUNC('month', NOW()) + INTERVAL '1 month' - INTERVAL '1 day')::date, NOW() - INTERVAL '6 days', false),
    (11, 'Personal', 4, NULL, 100, 76, DATE_TRUNC('month', NOW())::date, (DATE_TRUNC('month', NOW()) + INTERVAL '1 month' - INTERVAL '1 day')::date, NOW() - INTERVAL '5 days', false),
    (12, 'Personal', 8, NULL, 100, 100, DATE_TRUNC('month', NOW())::date, (DATE_TRUNC('month', NOW()) + INTERVAL '1 month' - INTERVAL '1 day')::date, NOW() - INTERVAL '4 days', false)
ON CONFLICT ("Id") DO NOTHING;
