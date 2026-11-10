-- 67. user_subscriptions (1) - phu huynh 2 dang o Goi Sang tao, chu ky 1 thang tu luc giao dich 7 Paid.
INSERT INTO user_subscriptions ("Id", "UserId", "PlanId", "StartAt", "EndAt", "Status", "PaymentId", "RenewalReminderSentAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 2, 3, NOW() - INTERVAL '9 days', NOW() - INTERVAL '9 days' + INTERVAL '1 month', 'Active', 7, NULL, NOW() - INTERVAL '9 days', false)
ON CONFLICT ("Id") DO NOTHING;
