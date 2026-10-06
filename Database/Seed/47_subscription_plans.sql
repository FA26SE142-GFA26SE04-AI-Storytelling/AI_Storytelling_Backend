-- 47. subscription_plans (1) - chi con goi Personal.
INSERT INTO subscription_plans ("Id", "Name", "PriceVnd", "QuotaAmount", "IsActive", "CreatedAt", "IsDeleted")
VALUES
    (1, 'Goi Personal', 49000, 50, true, NOW() - INTERVAL '60 days', false)
ON CONFLICT ("Id") DO NOTHING;
