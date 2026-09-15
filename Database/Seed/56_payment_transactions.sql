-- 56. payment_transactions (5)
INSERT INTO payment_transactions ("Id", "PlanId", "PayerUserId", "OrganizationId", "TransactionCode", "Amount", "Status", "SepayTransactionId", "QrCodeUrl", "CreatedAt", "ExpiresAt", "PaidAt", "IsDeleted")
VALUES
    (1, 1, 2, NULL, 'TOPUP-DEMO-0001', 49000, 'Paid', 'SEPAY-TX-0001', 'https://example.com/qr/1.png', NOW() - INTERVAL '9 days', NOW() - INTERVAL '9 days' + INTERVAL '15 minutes', NOW() - INTERVAL '9 days' + INTERVAL '3 minutes', false),
    (2, 2, 1, 1, 'TOPUP-DEMO-0002', 99000, 'Paid', 'SEPAY-TX-0002', 'https://example.com/qr/2.png', NOW() - INTERVAL '9 days', NOW() - INTERVAL '9 days' + INTERVAL '15 minutes', NOW() - INTERVAL '9 days' + INTERVAL '5 minutes', false),
    (3, 1, 3, NULL, 'TOPUP-DEMO-0003', 49000, 'Pending', NULL, 'https://example.com/qr/3.png', NOW() - INTERVAL '10 minutes', NOW() + INTERVAL '5 minutes', NULL, false),
    (4, 2, 1, 2, 'TOPUP-DEMO-0004', 99000, 'Expired', NULL, 'https://example.com/qr/4.png', NOW() - INTERVAL '2 days', NOW() - INTERVAL '2 days' + INTERVAL '15 minutes', NULL, false),
    (5, 1, 4, NULL, 'TOPUP-DEMO-0005', 45000, 'MismatchAmount', 'SEPAY-TX-0005', 'https://example.com/qr/5.png', NOW() - INTERVAL '5 days', NOW() - INTERVAL '5 days' + INTERVAL '15 minutes', NULL, false)
ON CONFLICT ("Id") DO NOTHING;
