-- 79. creator_balance_entries (1): thu nhap ky da chot (78) cua creator_demo (Id 13): 78.600 d, duoi muc chi
-- 200.000 d nen chua co trong danh sach chi va cong don sang ky sau (Luong 5 5h, AC2).
INSERT INTO creator_balance_entries (
    "Id", "CreatorUserId", "Type", "AmountVnd", "EarningPeriodId", "ReversedEntryId", "ValidReads",
    "BankTransactionCode", "TransferredOn", "BankName", "AccountNumber", "AccountHolderName", "RecordedByUserId",
    "Reason", "CancelledAt", "CreatedAt", "IsDeleted"
)
VALUES
    (1, 13, 'PeriodEarning', 78600, 1, NULL, 12, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL,
     date_trunc('month', (NOW() AT TIME ZONE 'Asia/Ho_Chi_Minh') - INTERVAL '1 month') AT TIME ZONE 'UTC' + INTERVAL '7 days',
     false)
ON CONFLICT ("Id") DO NOTHING;
