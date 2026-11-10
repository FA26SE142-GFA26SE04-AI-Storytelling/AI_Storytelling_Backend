-- 39. payment_transactions (5)
INSERT INTO payment_transactions ("Id", "PlanId", "PayerUserId", "TransactionCode", "Amount", "Status", "SepayTransactionId", "QrCodeUrl", "CreatedAt", "ExpiresAt", "PaidAt", "IsDeleted")
VALUES
    (1, 1, 2, 'TOPUP-DEMO-0001', 49000, 'Paid', 'SEPAY-TX-0001', 'https://example.com/qr/1.png', NOW() - INTERVAL '40 days', NOW() - INTERVAL '40 days' + INTERVAL '15 minutes', NOW() - INTERVAL '40 days' + INTERVAL '3 minutes', false),
    (3, 2, 3, 'SEPAYDEMO000003', 49000, 'Pending', NULL, 'https://example.com/qr/3.png', NOW() - INTERVAL '10 minutes', NOW() + INTERVAL '5 minutes', NULL, false),
    (5, 3, 4, 'SEPAYDEMO000005', 90000, 'MismatchAmount', 'SEPAY-TX-0005', 'https://example.com/qr/5.png', NOW() - INTERVAL '5 days', NOW() - INTERVAL '5 days' + INTERVAL '15 minutes', NULL, false),
    (7, 3, 2, 'SEPAYDEMO000007', 99000, 'Paid', 'SEPAY-TX-0007', 'https://example.com/qr/7.png', NOW() - INTERVAL '9 days', NOW() - INTERVAL '9 days' + INTERVAL '15 minutes', NOW() - INTERVAL '9 days' + INTERVAL '3 minutes', false),
    (8, 4, 2, 'SEPAYDEMO000008', 29000, 'Paid', 'SEPAY-TX-0008', 'https://example.com/qr/8.png', NOW() - INTERVAL '2 days', NOW() - INTERVAL '2 days' + INTERVAL '15 minutes', NOW() - INTERVAL '2 days' + INTERVAL '4 minutes', false)
ON CONFLICT ("Id") DO NOTHING;
