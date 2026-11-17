-- 77. creator_payout_accounts (1): thong tin nhan tien (du lieu demo, khong phai tai khoan that) cua creator_demo (Id 13),
-- Luong 5 5h.
INSERT INTO creator_payout_accounts (
    "Id", "CreatorUserId", "BankName", "AccountNumber", "AccountHolderName", "CreatedAt", "UpdatedAt", "IsDeleted"
)
VALUES
    (1, 13, 'Vietcombank', '0011000000013', 'LE THI MAI', NOW() - INTERVAL '30 days', NOW() - INTERVAL '30 days', false)
ON CONFLICT ("Id") DO NOTHING;
