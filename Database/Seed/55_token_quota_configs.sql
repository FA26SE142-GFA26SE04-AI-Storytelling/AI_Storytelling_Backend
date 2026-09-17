-- 55. token_quota_configs (5)
INSERT INTO token_quota_configs ("Id", "Scope", "OrganizationId", "ChildProfileId", "QuotaLimit", "QuotaUsed", "PeriodStart", "PeriodEnd", "CreatedAt", "IsDeleted")
VALUES
    (1, 'System', NULL, NULL, 10000, 3200, DATE_TRUNC('month', NOW())::date, (DATE_TRUNC('month', NOW()) + INTERVAL '1 month' - INTERVAL '1 day')::date, NOW() - INTERVAL '10 days', false),
    (2, 'Organization', 1, NULL, 200, 45, DATE_TRUNC('month', NOW())::date, (DATE_TRUNC('month', NOW()) + INTERVAL '1 month' - INTERVAL '1 day')::date, NOW() - INTERVAL '10 days', false),
    (3, 'Organization', 2, NULL, 200, 120, DATE_TRUNC('month', NOW())::date, (DATE_TRUNC('month', NOW()) + INTERVAL '1 month' - INTERVAL '1 day')::date, NOW() - INTERVAL '10 days', false),
    (4, 'Child', NULL, 1, 50, 12, DATE_TRUNC('month', NOW())::date, (DATE_TRUNC('month', NOW()) + INTERVAL '1 month' - INTERVAL '1 day')::date, NOW() - INTERVAL '10 days', false),
    (5, 'Child', NULL, 9, 50, 50, DATE_TRUNC('month', NOW())::date, (DATE_TRUNC('month', NOW()) + INTERVAL '1 month' - INTERVAL '1 day')::date, NOW() - INTERVAL '10 days', false)
ON CONFLICT ("Id") DO NOTHING;
