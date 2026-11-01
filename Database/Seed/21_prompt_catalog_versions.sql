-- 21. prompt_catalog_versions (5)
INSERT INTO prompt_catalog_versions ("Id", "VersionNo", "GradeBand", "EffectiveDate", "RestrictedKeywords", "Status", "CreatedByAdminId", "CreatedAt", "IsDeleted")
VALUES
    (1, 'v1.0', '6-8', NOW() - INTERVAL '55 days', 'bạo lực, sợ hãi quá mức', 'Deprecated', 1, NOW() - INTERVAL '55 days', false),
    (2, 'v1.1', '6-8', NOW() - INTERVAL '40 days', 'bạo lực, sợ hãi quá mức, phân biệt đối xử', 'Published', 1, NOW() - INTERVAL '40 days', false),
    (3, 'v1.0', '9-10', NOW() - INTERVAL '50 days', 'bạo lực cực đoan', 'RolledBack', 1, NOW() - INTERVAL '50 days', false),
    (4, 'v1.1', '9-10', NOW() - INTERVAL '30 days', 'bạo lực cực đoan, nội dung người lớn', 'Published', 1, NOW() - INTERVAL '30 days', false),
    (5, 'v1.2', '9-10', NOW() - INTERVAL '5 days', 'bạo lực cực đoan, nội dung người lớn, chính trị', 'Draft', 1, NOW() - INTERVAL '5 days', false)
ON CONFLICT ("Id") DO NOTHING;
