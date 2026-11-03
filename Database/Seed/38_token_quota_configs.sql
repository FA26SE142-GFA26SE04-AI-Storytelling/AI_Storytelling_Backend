-- 38. token_quota_configs (5) - scope System / Personal (khong con Organization).
INSERT INTO token_quota_configs ("Id", "Scope", "UserId", "QuotaLimit", "QuotaUsed", "PeriodStart", "PeriodEnd", "CreatedAt", "IsDeleted")
VALUES
    (1, 'System', NULL, 10000, 3200, DATE_TRUNC('month', NOW())::date, (DATE_TRUNC('month', NOW()) + INTERVAL '1 month' - INTERVAL '1 day')::date, NOW() - INTERVAL '10 days', false),
    (6, 'Personal', 2, 100, 18, DATE_TRUNC('month', NOW())::date, (DATE_TRUNC('month', NOW()) + INTERVAL '1 month' - INTERVAL '1 day')::date, NOW() - INTERVAL '8 days', false),
    (10, 'Personal', 3, 100, 0, DATE_TRUNC('month', NOW())::date, (DATE_TRUNC('month', NOW()) + INTERVAL '1 month' - INTERVAL '1 day')::date, NOW() - INTERVAL '6 days', false),
    (11, 'Personal', 4, 100, 76, DATE_TRUNC('month', NOW())::date, (DATE_TRUNC('month', NOW()) + INTERVAL '1 month' - INTERVAL '1 day')::date, NOW() - INTERVAL '5 days', false),
    (12, 'Personal', 8, 100, 100, DATE_TRUNC('month', NOW())::date, (DATE_TRUNC('month', NOW()) + INTERVAL '1 month' - INTERVAL '1 day')::date, NOW() - INTERVAL '4 days', false)
ON CONFLICT ("Id") DO NOTHING;
