-- 6. org_safety_policy_templates (3 - unique theo OrganizationId)
INSERT INTO org_safety_policy_templates ("Id", "OrganizationId", "MaxStoryLengthBaseline", "RequiredApprovalModeDefault", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 2000, 'AlwaysManual', NOW() - INTERVAL '48 days', false),
    (2, 2, 2500, 'AutoPublishOnThreshold', NOW() - INTERVAL '46 days', false),
    (3, 3, 1800, 'AlwaysManual', NOW() - INTERVAL '19 days', false)
ON CONFLICT ("Id") DO NOTHING;
