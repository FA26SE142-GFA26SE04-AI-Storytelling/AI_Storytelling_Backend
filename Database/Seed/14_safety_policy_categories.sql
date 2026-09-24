-- 14. safety_policy_categories (10)
INSERT INTO safety_policy_categories ("Id", "SafetyPolicyId", "ContentCategoryId", "Rule", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 1, 'Allowed', NOW() - INTERVAL '40 days', false),
    (2, 2, 2, 'Restricted', NOW() - INTERVAL '39 days', false),
    (3, 3, 3, 'Allowed', NOW() - INTERVAL '38 days', false),
    (4, 4, 4, 'Allowed', NOW() - INTERVAL '37 days', false),
    (5, 5, 5, 'Restricted', NOW() - INTERVAL '36 days', false),
    (6, 6, 6, 'Allowed', NOW() - INTERVAL '35 days', false),
    (7, 7, 1, 'Blocked', NOW() - INTERVAL '34 days', false),
    (8, 8, 2, 'Allowed', NOW() - INTERVAL '33 days', false),
    (9, 9, 3, 'Restricted', NOW() - INTERVAL '32 days', false),
    (10, 10, 4, 'Allowed', NOW() - INTERVAL '18 days', false)
ON CONFLICT ("Id") DO NOTHING;
