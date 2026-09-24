-- 29. prompt_catalog_versions (5)
INSERT INTO prompt_catalog_versions ("Id", "VersionNo", "GradeBand", "EffectiveDate", "RestrictedKeywords", "Status", "CreatedByAdminId", "CreatedAt", "IsDeleted")
VALUES
    (1, 'v1.0', '6-8', NOW() - INTERVAL '55 days', 'bao luc, so hai qua muc', 'Deprecated', 1, NOW() - INTERVAL '55 days', false),
    (2, 'v1.1', '6-8', NOW() - INTERVAL '40 days', 'bao luc, so hai qua muc, phan biet doi xu', 'Published', 1, NOW() - INTERVAL '40 days', false),
    (3, 'v1.0', '9-12', NOW() - INTERVAL '50 days', 'bao luc cuc doan', 'RolledBack', 1, NOW() - INTERVAL '50 days', false),
    (4, 'v1.1', '9-12', NOW() - INTERVAL '30 days', 'bao luc cuc doan, noi dung nguoi lon', 'Published', 1, NOW() - INTERVAL '30 days', false),
    (5, 'v1.2', '9-12', NOW() - INTERVAL '5 days', 'bao luc cuc doan, noi dung nguoi lon, chinh tri', 'Draft', 1, NOW() - INTERVAL '5 days', false)
ON CONFLICT ("Id") DO NOTHING;
