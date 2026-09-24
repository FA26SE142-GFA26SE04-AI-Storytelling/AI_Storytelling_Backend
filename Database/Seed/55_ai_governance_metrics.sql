-- 55. ai_governance_metrics (10 - 10 ngay gan nhat)
INSERT INTO ai_governance_metrics ("Id", "MetricDate", "AvgLatencyMs", "GenerationSuccessRate", "RegenerationRate", "SafetyFlagRate", "ApprovalRate", "CreatedAt", "IsDeleted")
VALUES
    (1, NOW() - INTERVAL '10 day', 1300, 92.500, 4.200, 2.100, 89.000, NOW() - INTERVAL '10 day', false),
    (2, NOW() - INTERVAL '9 day', 1280, 93.000, 4.000, 1.900, 90.000, NOW() - INTERVAL '9 day', false),
    (3, NOW() - INTERVAL '8 day', 1250, 93.500, 3.800, 1.700, 90.500, NOW() - INTERVAL '8 day', false),
    (4, NOW() - INTERVAL '7 day', 1220, 94.000, 3.600, 1.600, 91.000, NOW() - INTERVAL '7 day', false),
    (5, NOW() - INTERVAL '6 day', 1200, 94.500, 3.400, 1.400, 91.500, NOW() - INTERVAL '6 day', false),
    (6, NOW() - INTERVAL '5 day', 1180, 94.800, 3.300, 1.300, 91.800, NOW() - INTERVAL '5 day', false),
    (7, NOW() - INTERVAL '4 day', 1170, 95.000, 3.200, 1.200, 92.000, NOW() - INTERVAL '4 day', false),
    (8, NOW() - INTERVAL '3 day', 1150, 95.200, 3.100, 1.150, 92.200, NOW() - INTERVAL '3 day', false),
    (9, NOW() - INTERVAL '2 day', 1220, 95.300, 3.150, 1.120, 92.100, NOW() - INTERVAL '2 day', false),
    (10, NOW() - INTERVAL '1 day', 1200, 95.500, 3.200, 1.100, 92.000, NOW() - INTERVAL '1 day', false)
ON CONFLICT ("Id") DO NOTHING;
