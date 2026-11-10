-- 70. payment_reconciliation_exceptions (1) - giao dich Paid nhung ap goi loi, cho Admin xu ly (UC-L2-13).
INSERT INTO payment_reconciliation_exceptions ("Id", "PaymentId", "Status", "Reason", "ResolvedBy", "ResolvedAt", "CreatedAt", "IsDeleted")
VALUES
    (1, 1, 'Resolved', 'ApplyPlanThrew: transient database error', 1, NOW() - INTERVAL '39 days', NOW() - INTERVAL '40 days', false)
ON CONFLICT ("Id") DO NOTHING;
