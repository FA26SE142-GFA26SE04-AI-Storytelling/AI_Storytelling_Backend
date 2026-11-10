-- 69. credit_adjustments (2) - Admin cap / tru luot cho tai khoan phu huynh, luon kem ly do (UC-L2-08).
INSERT INTO credit_adjustments ("Id", "UserId", "Amount", "Reason", "AdminId", "CreatedAt", "IsDeleted")
VALUES
    (1, 3, 2, 'Bù lượt do lỗi sinh truyện ngày 05/11', 1, NOW() - INTERVAL '4 days', false),
    (2, 4, -1, 'Thu hồi lượt cấp nhầm', 1, NOW() - INTERVAL '3 days', false)
ON CONFLICT ("Id") DO NOTHING;
