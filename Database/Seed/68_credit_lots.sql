-- 68. credit_lots (6) - so luot AI: lo dung thu, lo chu ky, lo Add-on va luot con du cua mo hinh cu (Legacy).
INSERT INTO credit_lots ("Id", "UserId", "CreditType", "Source", "Total", "Remaining", "ExpiresAt", "PaymentId", "CreatedAt", "IsDeleted")
VALUES
    (1, 2, 'AI', 'Trial', 1, 0, NULL, NULL, NOW() - INTERVAL '50 days', false),
    (2, 2, 'AI', 'Legacy', 50, 32, NULL, NULL, NOW() - INTERVAL '40 days', false),
    (3, 2, 'AI', 'Cycle', 4, 3, NOW() - INTERVAL '9 days' + INTERVAL '1 month', 7, NOW() - INTERVAL '9 days', false),
    (4, 2, 'AI', 'AddOn', 2, 2, NOW() - INTERVAL '9 days' + INTERVAL '1 month', 8, NOW() - INTERVAL '2 days', false),
    (5, 3, 'AI', 'Trial', 1, 1, NULL, NULL, NOW() - INTERVAL '6 days', false),
    (6, 4, 'AI', 'Legacy', 100, 24, NULL, NULL, NOW() - INTERVAL '5 days', false)
ON CONFLICT ("Id") DO NOTHING;
