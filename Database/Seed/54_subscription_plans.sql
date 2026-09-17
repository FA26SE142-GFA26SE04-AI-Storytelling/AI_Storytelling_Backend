-- 54. subscription_plans (2)
INSERT INTO subscription_plans ("Id", "Name", "ApplicableScope", "PriceVnd", "QuotaAmount", "IsActive", "CreatedAt", "IsDeleted")
VALUES
    (1, 'Goi Personal', 'Personal', 49000, 50, true, NOW() - INTERVAL '60 days', false),
    (2, 'Goi Organization', 'Organization', 99000, 150, true, NOW() - INTERVAL '60 days', false)
ON CONFLICT ("Id") DO NOTHING;
