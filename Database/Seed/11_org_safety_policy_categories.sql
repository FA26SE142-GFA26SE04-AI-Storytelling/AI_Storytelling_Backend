-- 11. org_safety_policy_categories (8)
INSERT INTO org_safety_policy_categories ("Id", "OrgSafetyPolicyTemplateId", "ContentCategoryId", "Rule", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 1, 'Allowed', NOW() - INTERVAL '48 days', false),
    (2, 1, 2, 'Restricted', NOW() - INTERVAL '48 days', false),
    (3, 1, 3, 'Allowed', NOW() - INTERVAL '48 days', false),
    (4, 2, 1, 'Allowed', NOW() - INTERVAL '46 days', false),
    (5, 2, 4, 'Restricted', NOW() - INTERVAL '46 days', false),
    (6, 2, 5, 'Allowed', NOW() - INTERVAL '46 days', false),
    (7, 3, 2, 'Blocked', NOW() - INTERVAL '19 days', false),
    (8, 3, 6, 'Allowed', NOW() - INTERVAL '19 days', false)
ON CONFLICT ("Id") DO NOTHING;
