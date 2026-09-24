-- 59. token_quota_configs (6)
INSERT INTO token_quota_configs ("Id", "Scope", "UserId", "OrganizationId", "ChildProfileId", "QuotaLimit", "QuotaUsed", "PeriodStart", "PeriodEnd", "CreatedAt", "IsDeleted")
VALUES
    (1, 'System', NULL, NULL, NULL, 10000, 3200, DATE_TRUNC('month', NOW())::date, (DATE_TRUNC('month', NOW()) + INTERVAL '1 month' - INTERVAL '1 day')::date, NOW() - INTERVAL '10 days', false),
    (2, 'Organization', NULL, 1, NULL, 200, 45, DATE_TRUNC('month', NOW())::date, (DATE_TRUNC('month', NOW()) + INTERVAL '1 month' - INTERVAL '1 day')::date, NOW() - INTERVAL '10 days', false),
    (3, 'Organization', NULL, 2, NULL, 200, 120, DATE_TRUNC('month', NOW())::date, (DATE_TRUNC('month', NOW()) + INTERVAL '1 month' - INTERVAL '1 day')::date, NOW() - INTERVAL '10 days', false),
    (4, 'Child', NULL, NULL, 1, 50, 12, DATE_TRUNC('month', NOW())::date, (DATE_TRUNC('month', NOW()) + INTERVAL '1 month' - INTERVAL '1 day')::date, NOW() - INTERVAL '10 days', false),
    (5, 'Child', NULL, NULL, 9, 50, 50, DATE_TRUNC('month', NOW())::date, (DATE_TRUNC('month', NOW()) + INTERVAL '1 month' - INTERVAL '1 day')::date, NOW() - INTERVAL '10 days', false),
    (6, 'Personal', 2, NULL, NULL, 100, 18, DATE_TRUNC('month', NOW())::date, (DATE_TRUNC('month', NOW()) + INTERVAL '1 month' - INTERVAL '1 day')::date, NOW() - INTERVAL '8 days', false)
ON CONFLICT ("Id") DO NOTHING;
